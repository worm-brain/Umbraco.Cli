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
            "Unpublish many content items by id (ids from --file or stdin).\n\nExample:\n  umbraco content bulk unpublish --file ids.txt"
        ).Mutating();
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "File of ids (one per line). Reads stdin when omitted.",
        };
        var culturesOpt = ListOption.Strings(
            "--cultures",
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
                    ct
                );
            }
        );

        return cmd;
    }
}
