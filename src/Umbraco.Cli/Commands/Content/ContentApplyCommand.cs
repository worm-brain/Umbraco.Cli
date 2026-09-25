using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content apply</c> command (issue #100 / ADR 0006).</summary>
public static class ContentApplyCommand
{
    /// <summary>
    /// Builds the <c>content apply</c> command: reconciles the live instance towards a content
    /// snapshot. By default it creates, updates and carries publish state (#223; <c>--no-state</c>
    /// turns that off) - it never deletes. <c>--prune</c>
    /// additionally removes live documents the snapshot does not contain (within the snapshot's own
    /// scope), which makes the run destructive and therefore requires <c>--yes</c> non-interactively.
    /// <c>--dry-run</c> prints the full plan without writing anything; <c>--readonly</c> blocks the
    /// writes.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "apply",
            "Apply a content snapshot to the live instance (create, update and publish state; --prune also deletes).\n\n"
                + "Examples:\n"
                + "  umbraco content apply content.json --dry-run\n"
                + "  umbraco content apply content.json\n"
                + "  umbraco content apply content.json --no-state\n"
                + "  umbraco content apply content.json --prune --yes\n"
                + "  umbraco content apply content.json --prune --exclude-type contactSubmission --yes\n\n"
                + "A whole-tree prune also deletes whatever was created on the target since the "
                + "export - form submissions, editors' drafts. Export with --root to prune one "
                + "subtree, or leave content alone with --exclude-type / --exclude-root.\n\n"
                + "Publish state is applied too: cultures published in the snapshot are published, "
                + "and live cultures the snapshot has unpublished are unpublished."
        );
        var snapshotArg = new Argument<string>("snapshot")
        {
            Description = "Path to a snapshot file produced by 'content export', or '-' for stdin.",
        };
        var pruneOpt = new Option<bool>("--prune")
        {
            Description =
                "Also DELETE live documents (within the snapshot's scope) that the snapshot does "
                + "not contain. Destructive: requires --yes when non-interactive.",
        };
        // #225: a prune deletes everything the snapshot omits, including content created on the
        // target (form submissions). These leave chosen types and subtrees alone.
        var excludeTypeOpt = new Option<string[]>("--exclude-type")
        {
            Description =
                "With --prune, never delete documents of this document type (alias or id). Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };
        var excludeRootOpt = new Option<Guid[]>("--exclude-root")
        {
            Description =
                "With --prune, never delete this document or anything under it (id). Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };
        // #223: carrying publish state is the default, because the snapshot promises the target
        // will match it; this is the way out for a target whose publishing is done by hand.
        var noStateOpt = new Option<bool>("--no-state")
        {
            Description =
                "Do not publish or unpublish: new documents stay drafts and live publish state is left as it is.",
        };
        cmd.Add(snapshotArg);
        cmd.Add(pruneOpt);
        cmd.Add(noStateOpt);
        cmd.Add(excludeTypeOpt);
        cmd.Add(excludeRootOpt);
        // The exclusions only narrow a prune. Without --prune they would be silently ignored,
        // which reads as "these are protected"; refuse them instead.
        cmd.Validators.Add(result =>
        {
            var excludes =
                result.GetValue(excludeTypeOpt) is { Length: > 0 }
                || result.GetValue(excludeRootOpt) is { Length: > 0 };
            if (excludes && !result.GetValue(pruneOpt))
                result.AddError(
                    $"{excludeTypeOpt.Name} and {excludeRootOpt.Name} only apply with {pruneOpt.Name}."
                );
        });

        // Prune can delete live content, so it is gated behind the confirmation prompt (skipped
        // under --dry-run / --readonly by the executor). A non-prune apply only creates/updates
        // and is not gated, which is why the prompt is null without --prune. The prompt says what
        // is easy to miss: content created on the target goes too.
        cmd.DestructiveWith(
            pruneOpt,
            _ =>
                "This will DELETE live documents that are not present in the snapshot, "
                + "including any created on this instance since the export (form "
                + "submissions, drafts). Run with --dry-run first to see them."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var prune = parseResult.GetValue(pruneOpt);
                return executor.RunContextualAsync(
                    parseResult,
                    "content.apply",
                    async (ctx, c) =>
                    {
                        var diff = await ContentPipeline.DiffAgainstLiveAsync(
                            ctx.Client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        );
                        if (!diff.IsSuccess)
                            return UmbracoResponse<ContentApplyResult>.Failure(
                                diff.StatusCode,
                                diff.ErrorMessage!
                            );

                        var options = new ContentApplyOptions(prune, ctx.DryRun)
                        {
                            State = !parseResult.GetValue(noStateOpt),
                        };
                        // The exclusions only matter to a prune (the validator refuses them
                        // without one), so an alias is only resolved when there is a prune.
                        if (prune)
                        {
                            var exclude = await ResolveExclusionsAsync(
                                ctx.Client,
                                parseResult.GetValue(excludeTypeOpt) ?? [],
                                parseResult.GetValue(excludeRootOpt) ?? [],
                                c
                            );
                            if (!exclude.IsSuccess)
                                return UmbracoResponse<ContentApplyResult>.FailureFrom(exclude);
                            options = options with { Exclude = exclude.Data! };
                        }

                        // ctx.DryRun is honoured inside the applier so the *whole* plan is previewed.
                        return await ContentApplier.ApplyAsync(ctx.Client, diff.Data!, options, c);
                    },
                    (ctx, result) =>
                        ctx.Output.WriteTable(
                            new[] { "Operation", "Id", "Cultures", "Status" },
                            Rows(result),
                            ctx.CommandName,
                            ctx.Stopwatch.ElapsedMilliseconds
                        ),
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// Resolves the <c>--exclude-type</c> references (alias or id) to document type ids (#225).
    /// </summary>
    /// <param name="client">The client to resolve aliases with.</param>
    /// <param name="types">The document type references.</param>
    /// <param name="roots">The excluded root ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exclusions, or the failure of the first reference that did not resolve.</returns>
    private static async Task<UmbracoResponse<PruneExclusions>> ResolveExclusionsAsync(
        IUmbracoManagementClient client,
        IReadOnlyList<string> types,
        IReadOnlyList<Guid> roots,
        CancellationToken ct
    )
    {
        var typeIds = new HashSet<Guid>();
        foreach (var type in types)
        {
            var resolved = await client.GetDocumentTypeAsync(type, ct);
            if (!resolved.IsSuccess)
                return UmbracoResponse<PruneExclusions>.FailureFrom(resolved);
            typeIds.Add(resolved.Data!.Id);
        }
        return UmbracoResponse<PruneExclusions>.Success(
            new PruneExclusions(typeIds, roots.ToHashSet())
        );
    }

    /// <summary>Projects the apply result's steps into table rows (one per step).</summary>
    /// <param name="result">The apply result, or null on an unexpected empty payload.</param>
    /// <returns>The rows.</returns>
    private static IEnumerable<string[]> Rows(ContentApplyResult? result)
    {
        if (result is null)
            yield break;
        foreach (var a in result.Actions)
            yield return
            [
                a.Operation,
                a.Id.ToString(),
                a.Cultures is { } cultures ? string.Join(",", cultures) : "",
                a.Status,
            ];
    }
}
