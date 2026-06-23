using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("content", "Manage Umbraco content items.");
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
