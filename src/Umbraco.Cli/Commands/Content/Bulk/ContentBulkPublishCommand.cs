using System.CommandLine;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>Wires the <c>content bulk publish</c> command (issue #85).</summary>
public static class ContentBulkPublishCommand
{
    /// <summary>Builds the <c>content bulk publish</c> command (publish many ids; not gated — reversible).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "publish",
            "Publish many content items by id (ids from --file or stdin).\n\nExample:\n  umbraco content list --fields id | umbraco content bulk publish"
        );
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "File of ids (one per line). Reads stdin when omitted.",
        };
        var culturesOpt = new Option<string[]>("--cultures")
        {
            Description = "ISO culture codes to publish. Publishes all cultures if omitted.",
            AllowMultipleArgumentsPerToken = true,
        };
        cmd.Add(fileOpt);
        cmd.Add(culturesOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                var effective = cultures?.Length > 0 ? cultures : null;
                return executor.RunBulkAsync(
                    parseResult,
                    "content.bulk.publish",
                    () => BulkIds.Read(parseResult.GetValue(fileOpt)),
                    (client, id, c) => client.PublishContentAsync(id, effective, c),
                    ct
                );
            }
        );

        return cmd;
    }
}
