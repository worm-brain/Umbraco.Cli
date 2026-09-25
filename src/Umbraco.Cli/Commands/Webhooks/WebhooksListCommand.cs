using System.CommandLine;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all webhook subscriptions configured in the Umbraco instance.\n\nExample:\n  umbraco webhook list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetWebhooksAsync(skip, take, c),
                    ["ID", "URL", "Events", "Enabled"],
                    i =>
                        new[]
                        {
                            i.Id.ToString(),
                            i.Url,
                            // Events are objects (issue #46); show their names.
                            string.Join(", ", (i.Events ?? []).Select(e => e.EventName)),
                            i.Enabled.ToString(),
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
