using System.CommandLine;
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
    /// may contain a comma, so commas do not split it) and its parse-time shape check.
    /// </summary>
    /// <param name="cmd">The command to add it to.</param>
    /// <param name="description">The option's help text.</param>
    /// <returns>The option.</returns>
    public static Option<string[]> AddHeader(Command cmd, string description)
    {
        var option = new Option<string[]>("--header") { Description = description };
        cmd.Add(option);
        KeyValuePairs.Validate(cmd, option, "--header must be name=value, e.g. X-Api-Key=abc123");
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

    /// <summary>The parsed <c>--header</c> pairs as a name-to-value map; later pairs win.</summary>
    /// <param name="raw">The raw option values, or null when not given.</param>
    /// <returns>The headers; empty when none were given.</returns>
    public static Dictionary<string, string> Headers(string[]? raw)
    {
        // Header names are case-insensitive, so a repeated name is one header, not two.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in KeyValuePairs.Parse(raw))
            headers[name] = value;
        return headers;
    }
}
