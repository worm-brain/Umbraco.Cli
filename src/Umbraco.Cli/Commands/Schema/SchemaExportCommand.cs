using System.CommandLine;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Small structured summary emitted when <c>schema export</c> writes to a file (issue #68):
/// the per-kind counts and the destination path, so an agent gets machine-readable confirmation
/// rather than a bare message.
/// </summary>
/// <param name="DocumentTypes">Number of document types exported.</param>
/// <param name="MediaTypes">Number of media types exported (#186).</param>
/// <param name="MemberTypes">Number of member types exported (#186).</param>
/// <param name="DataTypes">Number of data types exported.</param>
/// <param name="Templates">Number of templates exported.</param>
/// <param name="Languages">Number of languages exported (#227).</param>
/// <param name="DictionaryItems">Number of dictionary items exported (#227).</param>
/// <param name="MemberGroups">Number of member groups exported (#227).</param>
/// <param name="UserGroups">Number of user groups exported (#227).</param>
/// <param name="Path">The absolute path the snapshot was written to.</param>
public sealed record SchemaExportSummary(
    int DocumentTypes,
    int MediaTypes,
    int MemberTypes,
    int DataTypes,
    int Templates,
    int Languages,
    int DictionaryItems,
    int MemberGroups,
    int UserGroups,
    string Path
);

/// <summary>Wires the <c>schema export</c> command (issue #68 / ADR 0005).</summary>
public static class SchemaExportCommand
{
    /// <summary>
    /// Builds the <c>schema export</c> command: dumps every document type, media type, member
    /// type, data type, template, language, dictionary item, member group and user group to a
    /// portable snapshot. With no <c>--out</c> the snapshot is written to stdout
    /// inside the normal success envelope (pipe/redirect friendly); with <c>--out &lt;file&gt;</c>
    /// the bare snapshot is written to that file and a count summary is emitted.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "export",
            "Export the schema to a portable JSON snapshot.\n\n"
                + "Covers document, media and member types, data types, templates, languages, "
                + "dictionary items, and member and user groups. User group start nodes and "
                + "per-document permissions are left out: they name content on this instance.\n\n"
                + "Examples:\n"
                + "  umbraco schema export --out schema.json\n"
                + "  umbraco schema export | jq '.data.documentTypes | length'"
        );
        var outOpt = new Option<FileInfo?>("--out", new[] { "-O" })
        {
            Description =
                "Write the snapshot to this file (bare JSON, no envelope). "
                + "Omit to write the snapshot to stdout inside the success envelope.",
        };
        cmd.Add(outOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunAsync(
                    parseResult,
                    (client, c) => SchemaExporter.ExportAsync(client, c),
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

                        // File target: write the bare snapshot (the artifact apply/diff consume)
                        // and report structured counts. A write failure here is caught by the
                        // executor and surfaced as a clean error.
                        File.WriteAllText(outFile.FullName, snapshot!.ToJson());
                        ctx.Output.WriteSuccess(
                            new SchemaExportSummary(
                                snapshot.DocumentTypes.Count,
                                snapshot.MediaTypes.Count,
                                snapshot.MemberTypes.Count,
                                snapshot.DataTypes.Count,
                                snapshot.Templates.Count,
                                snapshot.Languages.Count,
                                snapshot.DictionaryItems.Count,
                                snapshot.MemberGroups.Count,
                                snapshot.UserGroups.Count,
                                outFile.FullName
                            ),
                            ctx.CommandName,
                            ctx.Stopwatch.ElapsedMilliseconds
                        );
                    },
                    ct
                )
        );

        return cmd;
    }
}
