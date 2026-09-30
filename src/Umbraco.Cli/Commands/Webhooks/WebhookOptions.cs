using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary>
/// The options <c>webhook create</c> and <c>webhook update</c> share (#237): custom headers and the
/// type filter, and how each is turned into what the client sends.
/// </summary>
internal static class WebhookOptions
{
    /// <summary>
    /// Adds the repeat-only <c>--header name=value</c> option (docs/conventions.md 4.3: a value
    /// may contain a comma, so commas do not split it) and its parse-time checks: the shape, and
    /// each header name once, ignoring case as HTTP does (<c>X-Key</c> and <c>x-key</c> are one
    /// header).
    /// </summary>
    /// <param name="cmd">The command to add it to.</param>
    /// <param name="description">The option's help text.</param>
    /// <returns>The option.</returns>
    public static Option<string[]> AddHeader(Command cmd, string description)
    {
        var option = new Option<string[]>("--header") { Description = description };
        cmd.Add(option);
        KeyValuePairs.Validate(
            cmd,
            option,
            "--header must be name=value, e.g. X-Api-Key=abc123",
            StringComparer.OrdinalIgnoreCase
        );
        return option;
    }

    /// <summary>
    /// Adds the repeatable <c>--type</c> filter. It is <c>--type</c>, not <c>--document-type</c>,
    /// because a webhook's filter holds document types for content events, media types for media
    /// events and member types for member events (docs/conventions.md 4.1).
    /// </summary>
    /// <param name="cmd">The command to add it to.</param>
    /// <param name="description">The option's help text.</param>
    /// <returns>The option.</returns>
    public static Option<string[]> AddType(Command cmd, string description)
    {
        var option = ListOption.Strings("--type", description);
        cmd.Add(option);
        return option;
    }

    /// <summary>
    /// Rewrites an unknown <c>--type</c> alias refusal to name the nearest real alias (#368), as
    /// <see cref="WebhooksCreateCommand.WithEventSuggestions"/> does for <c>--event</c>. The client
    /// reports the unknown alias and every known one; the suggestion is the CLI's to make. Any
    /// other response is returned as is.
    /// </summary>
    /// <typeparam name="T">The response payload type.</typeparam>
    /// <param name="response">The type resolution's response.</param>
    /// <returns>The response, with the refusal message rewritten when it named an unknown alias.</returns>
    public static UmbracoResponse<T> WithTypeSuggestions<T>(UmbracoResponse<T> response)
    {
        if (response.IsSuccess || response.UnknownValues is not { } values)
            return response;

        var described = values.Unknown.Select(u =>
            Suggestions.Nearest(u, values.Known) is { } nearest
                ? $"'{u}' (did you mean '{nearest}'?)"
                : $"'{u}'"
        );
        return response with
        {
            ErrorMessage =
                $"No document type, media type or member type has the alias {string.Join(", ", described)}. "
                + "Use 'umbraco document-type list', 'media-type list' or 'member-type list' to "
                + "find one, or pass its id.",
        };
    }

    /// <summary>
    /// The parsed <c>--header</c> pairs as a name-to-value map, keyed ignoring case. A repeated
    /// name has already been refused at parse time, so each pair is its own entry.
    /// </summary>
    /// <param name="raw">The raw option values, or null when not given.</param>
    /// <returns>The headers; empty when none were given.</returns>
    public static Dictionary<string, string> Headers(string[]? raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in KeyValuePairs.Parse(raw))
            headers[name] = value;
        return headers;
    }
}
