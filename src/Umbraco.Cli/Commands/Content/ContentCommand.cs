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
            "Manage Umbraco content items (create, read, update, delete, publish).\n\nExamples:\n  umbraco content list --output json\n  umbraco content get <id>\n  umbraco content create --content-type textPage --name \"My Page\"\n  umbraco content publish <id>"
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
        cmd.Add(ContentVersionsCommand.Build(executor));
        cmd.Add(ContentVersionCommand.Build(executor));
        cmd.Add(ContentRollbackCommand.Build(executor));
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
