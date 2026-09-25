using System.CommandLine;
using Umbraco.Cli.Infrastructure.Output;

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
                executor.RunListAsync(
                    parseResult,
                    "content.diff",
                    (client, c) =>
                        ContentPipeline.DiffAgainstLiveAsync(
                            client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        ),
                    Rows,
                    new[] { "Change", "Id", "Parent", "Changes" },
                    change =>
                        [
                            change.Change.ToString(),
                            change.Id.ToString(),
                            change.Parent?.ToString() ?? "",
                            string.Join(", ", change.Changes ?? []),
                        ],
                    // A diff is complete by construction, so it can say so.
                    diff => new ListPaging(Rows(diff).Count, 0, null),
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// One row per reported change - added, changed, removed, and drifted (advisory placement
    /// drift); unchanged documents are omitted to keep the output lean. Structured output
    /// serializes the <see cref="ContentDocumentChange"/> records themselves (#229), so empty
    /// fields are null rather than <c>""</c> and each row carries its <c>changes</c>.
    /// </summary>
    /// <param name="diff">The computed diff, or null on an (unexpected) empty result.</param>
    /// <returns>The rows, one per change.</returns>
    private static IReadOnlyList<ContentDocumentChange> Rows(ContentDiff? diff) =>
        diff is null
            ? []
            : [.. diff.Added.Concat(diff.Changed).Concat(diff.Removed).Concat(diff.Drifted)];
}
