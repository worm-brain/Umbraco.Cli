using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("create", "Create a webhook.");
        var urlOpt = new Option<string>("--url") { Required = true };
        var eventsOpt = new Option<string[]>("--events")
        {
            Description =
                "Event names to subscribe to. Repeat --events for multiple: --events ContentPublished --events MediaSaved.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Optional human-readable name for the webhook.",
        };
        var descOpt = new Option<string?>("--description")
        {
            Description = "Optional description for the webhook.",
        };
        cmd.Add(urlOpt);
        cmd.Add(eventsOpt);
        cmd.Add(idOpt);
        cmd.Add(nameOpt);
        cmd.Add(descOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "webhooks.create",
                    (client, c) =>
                        client.CreateWebhookAsync(
                            new CreateWebhookRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt),
                                Description = parseResult.GetValue(descOpt),
                                Url = parseResult.GetValue(urlOpt)!,
                                Events = parseResult.GetValue(eventsOpt) ?? [],
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
