using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("content", "Manage Umbraco content items (create, read, update, delete, publish).\n\nExamples:\n  umbraco content list --output json\n  umbraco content get <id>\n  umbraco content create --content-type textPage --name \"My Page\"\n  umbraco content publish <id>");
        cmd.Add(ContentListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentUpdateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentPublishCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentUnpublishCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
