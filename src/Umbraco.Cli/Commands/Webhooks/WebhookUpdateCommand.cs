using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>
/// <c>webhook update &lt;id&gt;</c> (#237). Enabling and disabling are <c>--enabled true|false</c>
/// here rather than verbs of their own, since <c>enable</c>/<c>disable</c> are not in the verb
/// table (docs/conventions.md 2).
/// </summary>
public static class WebhookUpdateCommand
{
    /// <summary>
    /// Builds the <c>update</c> leaf. It merges (docs/conventions.md 5.1): omitted options keep
    /// their values, a list option given replaces that list, and headers merge by name. With
    /// <c>--replace</c> the given headers and types are the whole set, so omitting them clears
    /// them (declared destructive with it, 5.2). The merge itself is the client's, which reads the
    /// webhook before the <c>PUT</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a webhook by id or name. Omitted options keep their values.\n\n"
                + "--event and --type replace the current lists; --header merges by header name. "
                + "With --replace, the headers and types given are the whole set: any not given are removed.\n\n"
                + "Examples:\n  umbraco webhook update \"Deploy hook\" --enabled false\n  umbraco webhook update 3f7a8b2e-... --event Umbraco.ContentPublish --type blogPost\n  umbraco webhook update 3f7a8b2e-... --header X-Api-Key=abc123 --url https://my.app/hook\n  umbraco webhook update \"Deploy hook\" --replace --header X-Api-Key=abc123 --yes   # drop other headers and the type filter"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.Webhook);
        var urlOpt = new Option<string?>("--url")
        {
            Description = "New URL that Umbraco posts the event payload to.",
        };
        var eventsOpt = ListOption.Strings(
            "--event",
            "Event aliases that replace the current ones, e.g. Umbraco.ContentPublish. Checked against the instance's events; see 'webhook event list'."
        );
        var nameOpt = new Option<string?>("--name") { Description = "New name for the webhook." };
        var descOpt = new Option<string?>("--description")
        {
            Description = "New description for the webhook.",
        };
        var enabledOpt = new Option<bool?>("--enabled")
        {
            Description =
                "Enable (--enabled or --enabled true) or disable (--enabled false) the webhook.",
        };
        var replaceOpt = new Option<bool>("--replace")
        {
            Description =
                "Set exactly the headers and types given, removing any others; none given clears them. Without it headers merge and the type filter is kept.",
        };
        // Replacing drops whatever is not given, which the CLI cannot restore (docs/conventions.md 5.2).
        cmd.DestructiveWith(
            replaceOpt,
            _ => "Replace this webhook's headers and type filter, removing any not given?"
        );
        cmd.Add(idArg);
        cmd.Add(replaceOpt);
        cmd.Add(urlOpt);
        cmd.Add(eventsOpt);
        cmd.Add(nameOpt);
        cmd.Add(descOpt);
        cmd.Add(enabledOpt);
        var headerOpt = WebhookOptions.AddHeader(
            cmd,
            "A custom HTTP header sent with each delivery, as name=value. Repeat for several; each replaces a current header of the same name and the others are kept."
        );
        var typeOpt = WebhookOptions.AddType(
            cmd,
            "Document, media or member types (id or alias) that replace the current type filter: the webhook fires only for items of these types."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            async id =>
                            {
                                // Resolve the type aliases before anything is written, so a typo
                                // changes nothing.
                                IReadOnlyList<Guid>? types = null;
                                if (parseResult.GetValue(typeOpt) is { Length: > 0 } given)
                                {
                                    var resolved = await client.ResolveWebhookTypesAsync(given, c);
                                    if (!resolved.IsSuccess)
                                        return UmbracoResponse<WebhookResponse>.FailureFrom(
                                            resolved
                                        );
                                    types = resolved.Data;
                                }

                                var events = parseResult.GetValue(eventsOpt);
                                // An unknown event alias gets the same did-you-mean hint as create (#278).
                                return WebhooksCreateCommand.WithEventSuggestions(
                                    await client.UpdateWebhookAsync(
                                        id,
                                        new UpdateWebhookRequest
                                        {
                                            Url = parseResult.GetValue(urlOpt),
                                            Name = parseResult.GetValue(nameOpt),
                                            Description = parseResult.GetValue(descOpt),
                                            Enabled = parseResult.GetValue(enabledOpt),
                                            // An empty list means the option was not given.
                                            Events = events is { Length: > 0 } ? events : null,
                                            ContentTypeKeys = types,
                                            Headers = WebhookOptions.Headers(
                                                parseResult.GetValue(headerOpt)
                                            ),
                                            Replace = parseResult.GetValue(replaceOpt),
                                        },
                                        c
                                    )
                                );
                            },
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }
}
