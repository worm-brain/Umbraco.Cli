using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("content", "Manage Umbraco content items (create, read, update, delete, publish).\n\nExamples:\n  umbraco content list --output json\n  umbraco content get <id>\n  umbraco content create --content-type textPage --name \"My Page\"\n  umbraco content publish <id>");
        cmd.Add(ContentListCommand.Build(executor));
        cmd.Add(ContentGetCommand.Build(executor));
        cmd.Add(ContentCreateCommand.Build(executor));
        cmd.Add(ContentUpdateCommand.Build(executor));
        cmd.Add(ContentDeleteCommand.Build(executor));
        cmd.Add(ContentPublishCommand.Build(executor));
        cmd.Add(ContentUnpublishCommand.Build(executor));
        return cmd;
    }
}
