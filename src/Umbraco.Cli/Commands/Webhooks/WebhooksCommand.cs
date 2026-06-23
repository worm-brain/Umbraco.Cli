using System.CommandLine;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("webhooks", "Manage Umbraco webhooks.");
        cmd.Add(WebhooksListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(WebhooksCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(WebhooksDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
