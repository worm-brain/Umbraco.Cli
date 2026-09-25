using System.CommandLine;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "webhook",
            "Manage Umbraco webhook subscriptions for event notifications.\n\nExamples:\n  umbraco webhook list\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish,Umbraco.ContentUnpublish\n  umbraco webhook delete <id>"
        );
        cmd.Add(WebhooksListCommand.Build(executor));
        cmd.Add(WebhooksCreateCommand.Build(executor));
        cmd.Add(WebhooksDeleteCommand.Build(executor));
        return cmd;
    }
}
