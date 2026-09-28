using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>Wires the <c>schema apply</c> command (issue #68 / ADR 0005 §4).</summary>
public static class SchemaApplyCommand
{
    /// <summary>
    /// Builds the <c>schema apply</c> command: reconciles the live instance towards a snapshot.
    /// By default it only creates and updates — it never deletes. <c>--prune</c> additionally
    /// removes live entities the snapshot does not contain, which makes the run destructive and
    /// therefore requires <c>--yes</c> non-interactively. <c>--dry-run</c> prints the full plan
    /// without writing anything (unlike a normal write command's single-request preview, apply's
    /// dry run previews the whole multi-write plan); <c>--readonly</c> blocks the writes.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "apply",
            "Apply a schema snapshot to the live instance.\n\n"
                + "Creates and updates; --prune also deletes."
        )
            .WithExamples(
                "umbraco schema apply schema.json --dry-run",
                "umbraco schema apply schema.json",
                "umbraco schema apply schema.json --prune --yes"
            )
            .Mutating();
        var snapshotArg = new Argument<string>("snapshot")
        {
            Description = "Path to a snapshot file produced by 'schema export', or '-' for stdin.",
        };
        var pruneOpt = new Option<bool>("--prune")
        {
            Description =
                "Also DELETE live schema items (types, data types, templates, languages, "
                + "dictionary items, member and user groups, and the partial views, stylesheets "
                + "and scripts of a snapshot that has them) that the snapshot does not contain. "
                + "Destructive: requires --yes when non-interactive. A type still in use, a "
                + "language, a dictionary item with children the snapshot keeps, or a file a "
                + "template names is refused unless --force is given.",
        };
        var forceOpt = new Option<bool>(InUseGuard.ForceOption)
        {
            Description =
                "With --prune, delete them anyway. Umbraco deletes what they take with them: the "
                + "content of a type, a language's variants and translations, a dictionary item's "
                + "children, and a template's use of a deleted file.",
        };
        cmd.Add(snapshotArg);
        cmd.Add(pruneOpt);
        cmd.Add(forceOpt);
        // --force only overrides the prune's in-use check. Without --prune it would be silently
        // ignored, which reads as "this apply is forced"; refuse it instead.
        cmd.Validators.Add(result =>
        {
            if (result.GetValue(forceOpt) && !result.GetValue(pruneOpt))
                result.AddError($"{forceOpt.Name} only applies with {pruneOpt.Name}.");
        });

        // Prune can delete live schema, so it is gated behind the confirmation prompt (skipped
        // under --dry-run / --readonly by the executor). A non-prune apply only creates/updates
        // and is not gated, which is why the prompt is null without --prune.
        cmd.DestructiveWith(
            pruneOpt,
            _ =>
                "This will DELETE live schema items (types, data types, templates, languages, "
                + "dictionary items, member and user groups, and any files the snapshot manages) "
                + "that are not present in the snapshot."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var prune = parseResult.GetValue(pruneOpt);
                return executor.RunContextualAsync(
                    parseResult,
                    async (ctx, c) =>
                    {
                        var diff = await SchemaPipeline.DiffAgainstLiveAsync(
                            ctx.Client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        );
                        if (!diff.IsSuccess)
                            return UmbracoResponse<SchemaApplyResult>.Failure(
                                diff.StatusCode,
                                diff.ErrorMessage!
                            );

                        // ctx.DryRun is honoured inside the applier so the *whole* plan is
                        // previewed, not just the first write.
                        return await SchemaApplier.ApplyAsync(
                            ctx.Client,
                            diff.Data!,
                            new SchemaApplyOptions(
                                prune,
                                ctx.DryRun,
                                Force: parseResult.GetValue(forceOpt)
                            ),
                            c
                        );
                    },
                    // #229: the action records themselves, so structured output carries real nulls.
                    (ctx, result) =>
                        CommandExecutor.WriteReport(
                            ctx,
                            result?.Actions ?? [],
                            new[] { "Operation", "Kind", "Identity", "Id", "Status" },
                            a => [a.Operation, a.Kind, a.Identity, a.Id?.ToString() ?? "", a.Status]
                        ),
                    ct
                );
            }
        );

        return cmd;
    }
}
