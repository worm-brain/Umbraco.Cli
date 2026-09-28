using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media apply</c> command (#226, ADR 0008).</summary>
public static class MediaApplyCommand
{
    /// <summary>
    /// Builds <c>media apply</c>: creates and updates media items (and their files) so the target
    /// matches the snapshot, keeping every item's id. <c>--prune</c> also moves items the snapshot
    /// omits to the recycle bin; it is gated behind <c>--yes</c> because content that uses them
    /// stops showing them.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "apply",
            "Apply a media snapshot to the live instance.\n\n"
                + "Creates and updates items with their snapshot ids, uploading their files; "
                + "--prune also moves items the snapshot omits to the recycle bin. Media types must "
                + "already exist on the target (schema apply)."
        )
            .WithExamples(
                "umbraco media apply ./media-snapshot --dry-run",
                "umbraco media apply ./media-snapshot",
                "umbraco media apply ./media-snapshot --prune --yes"
            )
            .Mutating();
        var snapshotArg = MediaDiffCommand.SnapshotArgument();
        var verifyOpt = MediaDiffCommand.VerifyFilesOption();
        var pruneOpt = new Option<bool>("--prune")
        {
            Description =
                "Also move live media items (within the snapshot's scope) that the snapshot does "
                + "not contain to the recycle bin. Requires --yes when non-interactive.",
        };
        cmd.Add(snapshotArg);
        cmd.Add(verifyOpt);
        cmd.Add(pruneOpt);

        // Trashed media can be restored, but content that uses it stops showing it until then,
        // which takes live pages' images offline.
        cmd.DestructiveWith(
            pruneOpt,
            _ =>
                "This will move live media items that are not in the snapshot to the recycle bin, "
                + "including any uploaded on this instance since the export. Content that uses "
                + "them stops showing them. Run with --dry-run first to see them."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var prune = parseResult.GetValue(pruneOpt);
                return executor.RunContextualAsync(
                    parseResult,
                    async (ctx, c) =>
                    {
                        var diff = await MediaPipeline.DiffAgainstLiveAsync(
                            ctx.Client,
                            parseResult.GetValue(snapshotArg)!,
                            parseResult.GetValue(verifyOpt),
                            c
                        );
                        if (!diff.IsSuccess)
                            return UmbracoResponse<MediaApplyResult>.Failure(
                                diff.StatusCode,
                                diff.ErrorMessage!
                            );
                        // ctx.DryRun is honoured inside the applier so the whole plan is previewed.
                        return await MediaApplier.ApplyAsync(
                            ctx.Client,
                            diff.Data.Snapshot,
                            diff.Data.Diff,
                            prune,
                            ctx.DryRun,
                            c
                        );
                    },
                    (ctx, result) =>
                        CommandExecutor.WriteReport(
                            ctx,
                            result?.Actions ?? [],
                            new[] { "Operation", "Id", "File", "Status" },
                            a =>
                                [
                                    a.Operation.ToString().ToLowerInvariant(),
                                    a.Id.ToString(),
                                    a.File ?? "",
                                    a.Status,
                                ]
                        ),
                    ct
                );
            }
        );
        return cmd;
    }
}
