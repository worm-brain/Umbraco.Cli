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
        var cmd = new Command("create", "Create a webhook.")
            .WithExamples(
                "umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish",
                "umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish,Umbraco.MediaSave --name \"Deploy hook\"",
                "umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish --type blogPost --header X-Api-Key=abc123"
            )
            .Mutating();
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
            "A custom HTTP header sent with each delivery, as name=value. Repeat for several. A header with an empty value (name=) is not sent."
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
                        // Resolve the type aliases first, so a typo creates nothing. The events
                        // go along so a filter they can never match is refused too (#368).
                        var types = WebhookOptions.WithTypeSuggestions(
                            await client.ResolveWebhookTypesAsync(
                                parseResult.GetValue(typeOpt) ?? [],
                                parseResult.GetValue(eventsOpt) ?? [],
                                c
                            )
                        );
                        if (!types.IsSuccess)
                            return UmbracoResponse<WebhookResponse>.FailureFrom(types);

                        return WithEventSuggestions(
                            await client.CreateWebhookAsync(
                                new CreateWebhookRequest
                                {
                                    Id = parseResult.GetValue(idOpt),
                                    Name = parseResult.GetValue(nameOpt),
                                    Description = parseResult.GetValue(descOpt),
                                    Url = parseResult.GetValue(urlOpt)!,
                                    Events = parseResult.GetValue(eventsOpt) ?? [],
                                    // An empty value means "no such header", as on update
                                    // (#367); on a new webhook that is nothing to send.
                                    Headers = WebhookOptions
                                        .Headers(parseResult.GetValue(headerOpt))
                                        .Where(h => h.Value.Length > 0)
                                        .ToDictionary(StringComparer.OrdinalIgnoreCase),
                                    ContentTypeKeys = types.Data!,
                                },
                                c
                            )
                        );
                    },
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Rewrites an unknown-event refusal to name the nearest real alias for each unknown one and
    /// to point at <c>webhook event list</c> (#278). The client reports which aliases were unknown
    /// and which exist; the suggestion is the CLI's to make. Any other response is returned as is.
    /// </summary>
    /// <param name="response">The create's response.</param>
    /// <returns>The response, with the refusal message rewritten when it named unknown events.</returns>
    internal static UmbracoResponse<WebhookResponse> WithEventSuggestions(
        UmbracoResponse<WebhookResponse> response
    )
    {
        if (response.IsSuccess || response.UnknownValues is not { } values)
            return response;

        // Aliases are "Umbraco.ContentPublish"; people often type the bare "ContentPublished".
        var described = values.Unknown.Select(u =>
            Suggestions.Nearest(u, values.Known, optionalPrefix: "Umbraco.") is { } nearest
                ? $"'{u}' (did you mean '{nearest}'?)"
                : $"'{u}'"
        );
        return response with
        {
            ErrorMessage =
                $"Unknown webhook event {string.Join(", ", described)}. Umbraco would save the "
                + "webhook but never fire it. Run 'umbraco webhook event list' for the valid "
                + "aliases.",
        };
    }
}
