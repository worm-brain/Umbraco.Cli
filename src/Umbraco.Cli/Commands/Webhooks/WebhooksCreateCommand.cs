using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>Wires <c>webhook create</c>.</summary>
public static class WebhooksCreateCommand
{
    /// <summary>
    /// Builds <c>webhook create</c>: checks the <c>--event</c> aliases against the instance, then
    /// posts the webhook and returns it as <c>get</c> would show it.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a webhook.\n\nExamples:\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish\n  umbraco webhook create --url https://my.app/hook --event Umbraco.ContentPublish,Umbraco.MediaSave --name \"Deploy hook\""
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
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                        WithEventSuggestions(
                            await client.CreateWebhookAsync(
                                new CreateWebhookRequest
                                {
                                    Id = parseResult.GetValue(idOpt),
                                    Name = parseResult.GetValue(nameOpt),
                                    Description = parseResult.GetValue(descOpt),
                                    Url = parseResult.GetValue(urlOpt)!,
                                    Events = parseResult.GetValue(eventsOpt) ?? [],
                                },
                                c
                            )
                        ),
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
