using System.CommandLine;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all webhook subscriptions configured in the Umbraco instance.\n\nExample:\n  umbraco webhooks list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "webhooks.list",
                    (client, c) =>
                        client.GetWebhooksAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "URL", "Events", "Enabled"],
                    data =>
                        data?.Items.Select(i =>
                            new[]
                            {
                                i.Id.ToString(),
                                i.Url,
                                // Events are objects (issue #46); show their names.
                                string.Join(", ", (i.Events ?? []).Select(e => e.EventName)),
                                i.Enabled.ToString(),
                            }
                        )
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
