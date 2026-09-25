using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>Wires the <c>content bulk delete</c> command (issue #85).</summary>
public static class ContentBulkDeleteCommand
{
    /// <summary>
    /// Builds the <c>content bulk delete</c> command. Delete is permanent, so the batch is gated
    /// by a single confirmation prompt (requires <c>--yes</c> non-interactively) — it never
    /// prompts per item.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete many content items permanently, by id (ids from --file or stdin).\n\nExamples:\n  umbraco content bulk delete --file ids.txt --yes\n  cat ids.txt | umbraco content bulk delete --yes"
        ).Mutating();
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "File of ids (one per line). Reads stdin when omitted.",
        };
        cmd.Add(fileOpt);
        cmd.Destructive(parseResult =>
            "Permanently delete the supplied content items? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunBulkAsync(
                    parseResult,
                    () => BulkIds.Read(parseResult.GetValue(fileOpt)),
                    (client, id, c) => client.DeleteContentAsync(id, c),
                    ct
                )
        );

        return cmd;
    }
}
