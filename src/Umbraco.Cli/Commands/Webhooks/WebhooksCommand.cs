using System.CommandLine;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("webhooks", "Manage Umbraco webhook subscriptions for event notifications.\n\nExamples:\n  umbraco webhooks list\n  umbraco webhooks create --url https://my.app/hook --events ContentPublished ContentUnpublished\n  umbraco webhooks delete <id>");
        cmd.Add(WebhooksListCommand.Build(executor));
        cmd.Add(WebhooksCreateCommand.Build(executor));
        cmd.Add(WebhooksDeleteCommand.Build(executor));
        return cmd;
    }
}
