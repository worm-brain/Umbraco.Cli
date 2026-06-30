using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("create", "Create a webhook.");
        var urlOpt = new Option<string>("--url") { Required = true };
        var eventsOpt = new Option<string[]>("--events") { Description = "Event names to subscribe to. Repeat --events for multiple: --events ContentPublished --events MediaSaved.", Required = true, AllowMultipleArgumentsPerToken = true  };
        cmd.Add(urlOpt); cmd.Add(eventsOpt);
        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "webhooks.create",
            (client, c) => client.CreateWebhookAsync(new CreateWebhookRequest { Url = parseResult.GetValue(urlOpt)!, Events = parseResult.GetValue(eventsOpt) ?? [] }, c),
            ct));

        return cmd;
    }
}
