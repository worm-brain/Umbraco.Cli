using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>Wires the <c>schema diff</c> command (issue #68 / ADR 0004 §5).</summary>
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
                executor.RunTableAsync(
                    parseResult,
                    "schema.diff",
                    async (client, c) =>
                    {
                        // Load the desired snapshot (may throw -> clean error), then export the
                        // live schema to diff against. A failed export propagates as a failure.
                        var desired = await SchemaFile.LoadAsync(
                            parseResult.GetValue(snapshotArg)!,
                            c
                        );
                        var current = await SchemaExporter.ExportAsync(client, c);
                        if (!current.IsSuccess)
                            return UmbracoResponse<SchemaDiff>.Failure(
                                current.StatusCode,
                                current.ErrorMessage!
                            );

                        return UmbracoResponse<SchemaDiff>.Success(
                            SchemaDiffEngine.Compare(desired, current.Data!)
                        );
                    },
                    new[] { "Kind", "Change", "Identity", "Id Mismatch", "Note" },
                    diff => Flatten(diff),
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Flattens a diff into one table row per actionable change (added, changed, removed,
    /// skipped) across all three kinds; unchanged entities are omitted to keep the output lean.
    /// In JSON mode each row becomes an object keyed by the camelCased headers.
    /// </summary>
    /// <param name="diff">The computed diff, or null on an (unexpected) empty result.</param>
    /// <returns>The rows, one per change.</returns>
    private static IEnumerable<string[]> Flatten(SchemaDiff? diff)
    {
        if (diff is null)
            yield break;

        foreach (var kind in new[] { diff.DocumentTypes, diff.DataTypes, diff.Templates })
        {
            foreach (
                var change in kind
                    .Added.Concat(kind.Changed)
                    .Concat(kind.Removed)
                    .Concat(kind.Skipped)
            )
            {
                yield return
                [
                    change.Kind,
                    change.Change.ToString(),
                    change.Identity,
                    change.IdMismatch ? "yes" : "",
                    change.Note ?? "",
                ];
            }
        }
    }
}
