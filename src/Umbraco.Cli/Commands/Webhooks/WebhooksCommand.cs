using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>
/// The <c>webhook</c> noun: subscriptions, plus the <c>event</c> sub-noun listing valid aliases
/// and the <c>log</c> sub-noun listing deliveries.
/// </summary>
public static class WebhooksCommand
{
    /// <summary>Builds the <c>webhook</c> command group.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command group.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "webhook",
            "Manage Umbraco webhook subscriptions for event notifications."
        ).WithExamples(
            "umbraco webhook list",
            "umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish,Umbraco.ContentUnpublish",
            "umbraco webhook update <id> --enabled false",
            "umbraco webhook delete <id>",
            "umbraco webhook event list",
            "umbraco webhook log list <id>"
        );
        cmd.Add(WebhooksListCommand.Build(executor));
        cmd.Add(WebhookGetCommand.Build(executor));
        cmd.Add(WebhooksCreateCommand.Build(executor));
        cmd.Add(WebhookUpdateCommand.Build(executor));
        cmd.Add(WebhooksDeleteCommand.Build(executor));
        // The events a webhook can subscribe to are their own collection, so a sub-noun
        // (docs/conventions.md 1.4), mirroring 'content version'.
        var events = new Command(
            "event",
            "List the webhook events this instance can fire."
        ).WithExamples("umbraco webhook event list");
        events.Add(WebhookEventListCommand.Build(executor));
        cmd.Add(events);
        // The delivery log is another sub-resource, so another sub-noun (1.4).
        var log = new Command("log", "List webhook delivery attempts.").WithExamples(
            "umbraco webhook log list <id>",
            "umbraco webhook log list"
        );
        log.Add(WebhookLogListCommand.Build(executor));
        cmd.Add(log);
        return cmd;
    }
}
