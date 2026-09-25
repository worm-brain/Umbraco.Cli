using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Small structured summary emitted when <c>content export</c> writes to a file (issue #100): the
/// document count and destination path, so an agent gets machine-readable confirmation rather than
/// a bare message.
/// </summary>
/// <param name="Documents">Number of documents exported.</param>
/// <param name="Path">The absolute path the snapshot was written to.</param>
public sealed record ContentExportSummary(int Documents, string Path);

/// <summary>Wires the <c>content export</c> command (issue #100 / ADR 0006).</summary>
public static class ContentExportCommand
{
    /// <summary>
    /// Builds the <c>content export</c> command: dumps a content subtree to a portable snapshot.
    /// With no <c>--out</c> the snapshot is written to stdout inside the normal success envelope
    /// (pipe/redirect friendly); with <c>--out &lt;file&gt;</c> the bare snapshot is written to that
    /// file and a count summary is emitted. <c>--root &lt;id&gt;</c> limits the export to that
    /// document's subtree (the root is included as a portable top-level node); omit it to export the
    /// whole content tree.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "export",
            "Export a content subtree to a portable JSON snapshot.\n\n"
                + "Examples:\n"
                + "  umbraco content export --out content.json\n"
                + "  umbraco content export --root 3f7a8b2e-... --out subtree.json\n"
                + "  umbraco content export | jq '.data.documents | length'"
        );
        var rootOpt = new Option<Guid?>("--root")
        {
            Description =
                "Export only this document's subtree (root included). Omit for the whole tree.",
        };
        var outOpt = new Option<FileInfo?>("--out", new[] { "-O" })
        {
            Description =
                "Write the snapshot to this file (bare JSON, no envelope). "
                + "Omit to write the snapshot to stdout inside the success envelope.",
        };
        cmd.Add(rootOpt);
        cmd.Add(outOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var root = parseResult.GetValue(rootOpt);
                return executor.RunAsync(
                    parseResult,
                    (client, c) => ContentExporter.ExportAsync(client, root, c),
                    (ctx, snapshot) =>
                    {
                        var outFile = parseResult.GetValue(outOpt);
                        if (outFile is null)
                        {
                            // No file target: emit the snapshot as the envelope's data payload.
                            ctx.Output.WriteSuccess(
                                snapshot,
                                ctx.CommandName,
                                ctx.Stopwatch.ElapsedMilliseconds
                            );
                            return;
                        }

                        // File target: write the bare snapshot (the artifact apply/diff consume) and
                        // report a structured count. A write failure here is caught by the executor.
                        File.WriteAllText(outFile.FullName, snapshot!.ToJson());
                        ctx.Output.WriteSuccess(
                            new ContentExportSummary(snapshot.Documents.Count, outFile.FullName),
                            ctx.CommandName,
                            ctx.Stopwatch.ElapsedMilliseconds
                        );
                    },
                    ct
                );
            }
        );

        return cmd;
    }
}
