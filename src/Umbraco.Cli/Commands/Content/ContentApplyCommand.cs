using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content apply</c> command (issue #100 / ADR 0006).</summary>
public static class ContentApplyCommand
{
    /// <summary>
    /// Builds the <c>content apply</c> command: reconciles the live instance towards a content
    /// snapshot. By default it only creates and updates - it never deletes. <c>--prune</c>
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
            "Apply a content snapshot to the live instance (create + update; --prune also deletes).\n\n"
                + "Examples:\n"
                + "  umbraco content apply content.json --dry-run\n"
                + "  umbraco content apply content.json\n"
                + "  umbraco content apply content.json --prune --yes"
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
        cmd.Add(snapshotArg);
        cmd.Add(pruneOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var prune = parseResult.GetValue(pruneOpt);
                // Prune can delete live content, so gate it behind the confirmation prompt (skipped
                // under --dry-run / --readonly by the executor). A non-prune apply only creates/
                // updates and is not gated.
                var confirmation = prune
                    ? "This will DELETE live documents that are not present in the snapshot."
                    : null;

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

                        // ctx.DryRun is honoured inside the applier so the *whole* plan is previewed.
                        return await ContentApplier.ApplyAsync(
                            ctx.Client,
                            diff.Data!,
                            prune,
                            ctx.DryRun,
                            c
                        );
                    },
                    (ctx, result) =>
                        ctx.Output.WriteTable(new[] { "Operation", "Id", "Status" }, Rows(result)),
                    ct,
                    confirmation
                );
            }
        );

        return cmd;
    }

    /// <summary>Projects the apply result's steps into table rows (one per create/update/delete).</summary>
    /// <param name="result">The apply result, or null on an unexpected empty payload.</param>
    /// <returns>The rows.</returns>
    private static IEnumerable<string[]> Rows(ContentApplyResult? result)
    {
        if (result is null)
            yield break;
        foreach (var a in result.Actions)
            yield return [a.Operation, a.Id.ToString(), a.Status];
    }
}
