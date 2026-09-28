using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>
/// <c>webhook event list</c> (#234): the event aliases this instance can fire, which are the only
/// values <c>webhook create --event</c> accepts.
/// </summary>
public static class WebhookEventListCommand
{
    /// <summary>Builds the <c>list</c> leaf under <c>webhook event</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List the event aliases that 'webhook create --event' accepts."
        ).WithExamples(
            "umbraco webhook event list",
            "umbraco webhook event list -o json | jq -r '.data[].alias'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetWebhookEventsAsync(skip, take, c),
                    ["Alias", "Name", "Type"],
                    e => new[] { e.Alias ?? "", e.EventName ?? "", e.EventType ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
