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
                executor.RunTableAsync(
                    parseResult,
                    "content.diff",
                    (client, c) =>
                        ContentPipeline.DiffAgainstLiveAsync(
                            client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        ),
                    new[] { "Change", "Id", "Parent" },
                    diff => Flatten(diff),
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Flattens a diff into one table row per reported change - added, changed, removed, and drifted
    /// (advisory placement drift); unchanged documents are omitted to keep the output lean. In JSON
    /// mode each row becomes an object keyed by the camelCased headers.
    /// </summary>
    /// <param name="diff">The computed diff, or null on an (unexpected) empty result.</param>
    /// <returns>The rows, one per change.</returns>
    private static IEnumerable<string[]> Flatten(ContentDiff? diff)
    {
        if (diff is null)
            yield break;

        foreach (
            var change in diff.Added.Concat(diff.Changed).Concat(diff.Removed).Concat(diff.Drifted)
        )
        {
            yield return
            [
                change.Change.ToString(),
                change.Id.ToString(),
                change.Parent?.ToString() ?? "",
            ];
        }
    }
}
