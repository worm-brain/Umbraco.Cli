using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content diff</c> command (issue #100 / ADR 0006).</summary>
public static class ContentDiffCommand
{
    /// <summary>
    /// Builds the <c>content diff</c> command: exports the live content (at the snapshot's own
    /// scope) and compares it against a snapshot file, printing every actionable difference
    /// (create/update/delete). This is a read-only operation - it never writes. Output is a table
    /// for humans and, in JSON mode, a row per change (empty when the instance already matches).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "diff",
            "Compare a content snapshot against the live instance (read-only).\n\n"
                + "Examples:\n"
                + "  umbraco content diff content.json\n"
                + "  umbraco content export | umbraco content diff -"
        );
        var snapshotArg = new Argument<string>("snapshot")
        {
            Description = "Path to a snapshot file produced by 'content export', or '-' for stdin.",
        };
        cmd.Add(snapshotArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunReportAsync(
                    parseResult,
                    (client, c) =>
                        ContentPipeline.DiffAgainstLiveAsync(
                            client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        ),
                    diff => diff?.Rows ?? [],
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
