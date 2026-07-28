using System.CommandLine;

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
            "Permanently delete many content items by id (ids from --file or stdin).\n\nExample:\n  umbraco content bulk delete --file ids.txt --yes"
        );
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "File of ids (one per line). Reads stdin when omitted.",
        };
        cmd.Add(fileOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunBulkAsync(
                    parseResult,
                    "content.bulk.delete",
                    () => BulkIds.Read(parseResult.GetValue(fileOpt)),
                    (client, id, c) => client.DeleteContentAsync(id, c),
                    ct,
                    confirmationPrompt: "Permanently delete the supplied content items? This cannot be undone."
                )
        );

        return cmd;
    }
}
