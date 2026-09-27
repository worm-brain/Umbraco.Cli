using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>Wires the <c>schema diff</c> command (issue #68 / ADR 0005 §5).</summary>
public static class SchemaDiffCommand
{
    /// <summary>
    /// Builds the <c>schema diff</c> command: exports the live schema and compares it against a
    /// snapshot file, printing every actionable difference (create/update/delete/skip). This is
    /// a read-only operation — it never writes. Output is a table for humans and, in JSON mode, a
    /// row per change (empty when the instance already matches the snapshot).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "diff",
            "Compare a schema snapshot against the live instance (read-only).\n\n"
                + "Examples:\n"
                + "  umbraco schema diff schema.json\n"
                + "  umbraco schema export | umbraco schema diff -"
        );
        var snapshotArg = new Argument<string>("snapshot")
        {
            Description = "Path to a snapshot file produced by 'schema export', or '-' for stdin.",
        };
        cmd.Add(snapshotArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunReportAsync(
                    parseResult,
                    (client, c) =>
                        SchemaPipeline.DiffAgainstLiveAsync(
                            client,
                            parseResult.GetValue(snapshotArg)!,
                            c
                        ),
                    Rows,
                    new[]
                    {
                        "Kind",
                        "Change",
                        "Identity",
                        "Desired Id",
                        "Current Id",
                        "Id Mismatch",
                        "Note",
                        "Changes",
                    },
                    change =>
                        [
                            change.Kind,
                            change.Change.ToString(),
                            change.Identity,
                            change.DesiredId?.ToString() ?? "",
                            change.CurrentId?.ToString() ?? "",
                            change.IdMismatch ? "yes" : "",
                            change.Note ?? "",
                            string.Join(", ", change.Changes ?? []),
                        ],
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// One row per actionable change (added, changed, removed, skipped) across every kind;
    /// unchanged entities are omitted to keep the output lean. Structured output serializes the
    /// <see cref="SchemaEntityChange"/> records themselves (#229).
    /// </summary>
    /// <param name="diff">The computed diff, or null on an (unexpected) empty result.</param>
    /// <returns>The rows, one per change.</returns>
    private static IReadOnlyList<SchemaEntityChange> Rows(SchemaDiff? diff) =>
        diff is null
            ? []
            :
            [
                .. diff.Kinds.SelectMany(kind =>
                    kind.Added.Concat(kind.Changed).Concat(kind.Removed).Concat(kind.Skipped)
                ),
            ];
}
