using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media diff</c> command (#226, ADR 0008).</summary>
public static class MediaDiffCommand
{
    /// <summary>The <c>snapshot</c> argument shared by <c>media diff</c> and <c>media apply</c>.</summary>
    /// <returns>A new argument.</returns>
    internal static Argument<string> SnapshotArgument() =>
        new("snapshot")
        {
            Description =
                "The snapshot directory 'media export' wrote (or its media.json). Not stdin: the "
                + "files are part of it.",
        };

    /// <summary>The <c>--verify-files</c> option shared by <c>media diff</c> and <c>media apply</c>.</summary>
    /// <returns>A new option.</returns>
    internal static Option<bool> VerifyFilesOption() =>
        new("--verify-files")
        {
            Description =
                "Download each live file the snapshot also has and compare SHA-256 hashes. "
                + "Without it, files are compared by name and size.",
        };

    /// <summary>
    /// Builds <c>media diff</c>: reads the live media at the snapshot's scope and lists every item
    /// that differs. Read-only.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "diff",
            "Compare a media snapshot against the live instance (read-only).\n\n"
                + "Examples:\n"
                + "  umbraco media diff ./media-snapshot\n"
                + "  umbraco media diff ./media-snapshot --verify-files"
        );
        var snapshotArg = SnapshotArgument();
        var verifyOpt = VerifyFilesOption();
        cmd.Add(snapshotArg);
        cmd.Add(verifyOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunReportAsync(
                    parseResult,
                    (client, c) =>
                        MediaPipeline.DiffAgainstLiveAsync(
                            client,
                            parseResult.GetValue(snapshotArg)!,
                            parseResult.GetValue(verifyOpt),
                            c
                        ),
                    result => result.Diff?.Rows ?? [],
                    new[] { "Change", "Id", "Parent", "Changes" },
                    change =>
                        [
                            change.Change.ToString(),
                            change.Id.ToString(),
                            change.Parent?.ToString() ?? "",
                            string.Join(", ", change.Changes ?? []),
                        ],
                    ct
                )
        );
        return cmd;
    }
}
