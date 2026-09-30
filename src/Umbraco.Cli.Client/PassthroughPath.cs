namespace Umbraco.Cli.Client;

/// <summary>
/// The one rule for which paths <c>umbraco api</c> may request (ADR 0010): a path under
/// <c>/umbraco/</c> on the configured host, and nothing that could leave it. The command checks it
/// at parse time so a bad path fails before any login, and <see cref="IPassthroughClient"/> checks
/// it again before sending, so the passthrough cannot become a general HTTP client whoever calls it.
/// </summary>
public static class PassthroughPath
{
    /// <summary>The prefix every allowed path starts with, compared ignoring case.</summary>
    public const string Prefix = "/umbraco/";

    /// <summary>
    /// What is wrong with <paramref name="path"/>, or null when it may be requested. Refused: a
    /// full URL or protocol-relative path (it would not start <c>/umbraco/</c>), a <c>.</c> or
    /// <c>..</c> segment or an encoded dot (either could climb out of <c>/umbraco/</c> once the URL
    /// is normalised), a backslash, a fragment, and whitespace or control characters.
    /// </summary>
    /// <param name="path">The path from the host root, with any query string.</param>
    /// <returns>A message saying what to change, or null for an allowed path.</returns>
    public static string? Problem(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "Give the request path, starting /umbraco/.";

        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return $"'{path}' is not a path under /umbraco/. Give the path from the site's root, "
                + "such as /umbraco/management/api/v1/server/status, not a URL: only the site's "
                + "own /umbraco/ routes can be requested.";

        if (path.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return $"'{path}' contains whitespace or a control character; percent-encode it.";

        if (path.Contains('\\') || path.Contains('#'))
            return $"'{path}' contains a backslash or a '#'; neither belongs in a request path.";

        // Only the part before the query string is a path: a '.' in a query value is fine.
        var segments = path.Split('?', 2)[0];
        if (
            segments.Contains("%2e", StringComparison.OrdinalIgnoreCase)
            || segments.Split('/').Any(s => s is "." or "..")
        )
            return $"'{path}' contains a '.' or '..' segment; give the path without them.";

        return null;
    }
}
