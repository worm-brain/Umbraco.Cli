using System.CommandLine;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>Wires the <c>content bulk</c> command group (issue #85).</summary>
public static class ContentBulkCommand
{
    /// <summary>
    /// Builds the <c>content bulk</c> noun: run delete/publish/unpublish over many ids read from
    /// stdin or a file, emitting a per-item results array so a script sees what happened to each.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "bulk",
            "Run an operation over many content ids read from stdin or a file.\n\nIds are one per line, or the JSON or CSV output of a list command. Each id gets its own result.\n\nExamples:\n  umbraco content list --fields id | umbraco content bulk publish\n  umbraco content bulk delete --file ids.txt --yes"
        );
        cmd.Add(ContentBulkDeleteCommand.Build(executor));
        cmd.Add(ContentBulkPublishCommand.Build(executor));
        cmd.Add(ContentBulkUnpublishCommand.Build(executor));
        return cmd;
    }
}
