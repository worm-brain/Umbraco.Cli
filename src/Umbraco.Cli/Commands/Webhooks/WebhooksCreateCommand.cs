using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary><c>webhook create</c>: subscribe a URL to one or more events.</summary>
public static class WebhooksCreateCommand
{
    /// <summary>
    /// Builds the <c>create</c> leaf. Custom headers and the type filter (#237) are optional; with
    /// no <c>--type</c> the webhook fires for items of every type.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a webhook.\n\nExamples:\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish,Umbraco.MediaSave --name \"Deploy hook\"\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish --type blogPost --header X-Api-Key=abc123"
        ).Mutating();
        var urlOpt = new Option<string>("--url")
        {
            Required = true,
            Description = "URL that Umbraco posts the event payload to.",
        };
        var eventsOpt = ListOption
            .Strings(
                "--event",
                "Umbraco event aliases to subscribe to, e.g. Umbraco.ContentPublish or Umbraco.MediaSave. Checked against the instance's events; see 'webhook event list'."
            )
            .AsRequired();
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
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
        var headerOpt = WebhookOptions.AddHeader(
            cmd,
            "A custom HTTP header sent with each delivery, as name=value. Repeat for several."
        );
        var typeOpt = WebhookOptions.AddType(
            cmd,
            "Document, media or member types (id or alias) the webhook fires for; omit to fire for every type."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // Resolve the type aliases first, so a typo creates nothing.
                        var types = await client.ResolveWebhookTypesAsync(
                            parseResult.GetValue(typeOpt) ?? [],
                            c
                        );
                        if (!types.IsSuccess)
                            return UmbracoResponse<WebhookResponse>.FailureFrom(types);

                        return await client.CreateWebhookAsync(
                            new CreateWebhookRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt),
                                Description = parseResult.GetValue(descOpt),
                                Url = parseResult.GetValue(urlOpt)!,
                                Events = parseResult.GetValue(eventsOpt) ?? [],
                                Headers = WebhookOptions.Headers(parseResult.GetValue(headerOpt)),
                                ContentTypeKeys = types.Data!,
                            },
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }
}
