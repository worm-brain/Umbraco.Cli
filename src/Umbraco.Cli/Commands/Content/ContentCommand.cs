using System.CommandLine;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content.Bulk;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "content",
            "Manage Umbraco content items (create, read, update, delete, publish).\n\nExamples:\n  umbraco content list --output json\n  umbraco content get <id>\n  umbraco content create --document-type textPage --name \"My Page\"\n  umbraco content publish <id>"
        );
        cmd.Add(ContentListCommand.Build(executor));
        cmd.Add(ContentTreeCommand.Build(executor));
        cmd.Add(ContentFindCommand.Build(executor));
        cmd.Add(ContentGetCommand.Build(executor));
        cmd.Add(ContentCreateCommand.Build(executor));
        cmd.Add(ContentUpdateCommand.Build(executor));
        cmd.Add(ContentDeleteCommand.Build(executor));
        cmd.Add(ContentPublishCommand.Build(executor));
        cmd.Add(ContentDomainsCommand.Build(executor));
        cmd.Add(ContentUnpublishCommand.Build(executor));
        // A content item's versions are one sub-resource, so one sub-noun (docs/conventions.md 1.4).
        var version = new Command(
            "version",
            "List, inspect and roll back a content item's versions.\n\nExamples:\n  umbraco content version list <id>\n  umbraco content version get <version-id>\n  umbraco content version rollback <version-id>"
        );
        version.Add(ContentVersionsCommand.Build(executor));
        version.Add(ContentVersionCommand.Build(executor));
        version.Add(ContentRollbackCommand.Build(executor));
        cmd.Add(version);
        cmd.Add(ContentTrashCommand.Build(executor));
        cmd.Add(ContentRestoreCommand.Build(executor));
        cmd.Add(ContentEmptyRecycleBinCommand.Build(executor));
        cmd.Add(ContentMoveCommand.Build(executor));
        cmd.Add(ContentSortCommand.Build(executor));
        cmd.Add(ContentCopyCommand.Build(executor));
        cmd.Add(ContentPublishDescendantsCommand.Build(executor));
        cmd.Add(ContentBulkCommand.Build(executor));
        cmd.Add(ContentExportCommand.Build(executor));
        cmd.Add(ContentDiffCommand.Build(executor));
        cmd.Add(ContentApplyCommand.Build(executor));
        return cmd;
    }
}
