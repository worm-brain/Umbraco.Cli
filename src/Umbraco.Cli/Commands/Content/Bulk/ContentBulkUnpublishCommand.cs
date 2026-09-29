using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>Wires the <c>content bulk unpublish</c> command (issue #85).</summary>
public static class ContentBulkUnpublishCommand
{
    /// <summary>
    /// Builds the <c>content bulk unpublish</c> command (unpublish many ids). Gated by a single
    /// batch confirmation because taking live content offline is high production impact (#82).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "unpublish",
            "Unpublish many content items by id (ids from --file or stdin)."
        )
            .WithExamples(
                "umbraco content bulk unpublish --file ids.txt",
                "umbraco content bulk unpublish --file ids.txt --culture da-DK"
            )
            .Mutating();
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description =
                "File of ids: one per line, or the JSON or CSV output of a list command. Reads stdin when omitted or -.",
        };
        var culturesOpt = ListOption.Strings(
            "--culture",
            "ISO culture codes to unpublish. Unpublishes all cultures if omitted."
        );
        cmd.Add(fileOpt);
        cmd.Add(culturesOpt);
        cmd.Destructive(parseResult =>
            "Unpublish the supplied content items, taking them offline? Re-publish to restore."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                var effective = cultures?.Length > 0 ? cultures : null;
                return executor.RunBulkAsync(
                    parseResult,
                    () => BulkIds.Read(parseResult.GetValue(fileOpt)),
                    (client, id, c) => client.UnpublishContentAsync(id, effective, c),
                    ct,
                    // No culture named: every document's cultures in one batch read (#414).
                    effective is null
                        ? (client, ids, c) => client.PrefetchPublishCulturesAsync(ids, c)
                        : null
                );
            }
        );

        return cmd;
    }
}
