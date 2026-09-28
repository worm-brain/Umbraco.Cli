using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>
/// <c>webhook log list [&lt;id&gt;]</c> (#237): the delivery log, a sub-noun of <c>webhook</c>
/// (docs/conventions.md 1.4). With an id it is that webhook's log; without, every webhook's.
/// </summary>
public static class WebhookLogListCommand
{
    /// <summary>Builds the <c>list</c> leaf under <c>webhook log</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List webhook delivery attempts: what was sent and what came back.\n\n"
                + "Give a webhook to see only its deliveries; omit it to see every webhook's."
        ).WithExamples(
            "umbraco webhook log list \"Deploy hook\"",
            "umbraco webhook log list",
            "umbraco webhook log list 3f7a8b2e-... -o json | jq '.data[] | select(.isSuccessStatusCode | not)'"
        );
        // Optional: the log endpoint has an all-webhooks form, and 'log list' with no webhook is
        // the natural way to ask "what failed recently?".
        var idArg = Reference.Argument(EntityKind.Webhook);
        idArg.Description =
            "The webhook's id, or its name. Omit to list every webhook's deliveries.";
        idArg.Arity = ArgumentArity.ZeroOrOne;
        cmd.Add(idArg);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.WithResolvedOptionalAsync(
                            EntityKind.Webhook,
                            parseResult.GetValue(idArg),
                            id => client.GetWebhookLogsAsync(id, skip, take, c),
                            c
                        ),
                    ["Date", "Status", "Event", "Retries", "URL"],
                    l =>
                        new[]
                        {
                            l.Date?.ToString("u") ?? "",
                            l.StatusCode ?? "",
                            l.EventAlias ?? "",
                            l.RetryCount.ToString(),
                            l.Url ?? "",
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }
}
