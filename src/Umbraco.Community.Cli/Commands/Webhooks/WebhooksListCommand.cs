using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Webhooks;

/// <summary>Wires the <c>webhook list</c> command.</summary>
public static class WebhooksListCommand
{
    /// <summary>Builds the <c>webhook list</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all webhook subscriptions configured in the Umbraco instance."
        ).WithExamples("umbraco webhook list --output json");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
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
