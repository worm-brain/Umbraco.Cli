namespace Umbraco.Cli.Client;

/// <summary>
/// The rules a host must meet before the CLI sends it a credential (a client secret or a bearer
/// token). Kept in one place so the token exchange, the command pipeline and <c>auth doctor</c>
/// apply the same policy.
/// </summary>
public static class HostPolicy
{
    /// <summary>
    /// Why credentials must not be sent to <paramref name="host"/> over its transport, or null when
    /// they may. Plain <c>http://</c> is refused unless the host is loopback (<c>localhost</c>,
    /// <c>127.0.0.1</c>, <c>::1</c>), because anyone on the network path could read the secret or
    /// token. There is deliberately no opt-out: a remote instance has to be served over HTTPS.
    /// A host that is not an absolute URL is left to the caller's own validation.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <returns>A one-line error message, or null when the transport is acceptable.</returns>
    public static string? InsecureTransportError(string host)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp || uri.IsLoopback)
            return null;
        return $"Refusing to send credentials to '{host}' over plain HTTP. Use an https:// URL "
            + "(http:// is only allowed for localhost, 127.0.0.1 and ::1).";
    }

    /// <summary>
    /// Whether two host URLs name the same instance, compared the way the token cache keys them:
    /// case-insensitively and ignoring trailing slashes.
    /// </summary>
    /// <param name="a">One host URL.</param>
    /// <param name="b">The other host URL.</param>
    /// <returns>True when both are set and equal after normalisation.</returns>
    public static bool IsSameHost(string? a, string? b) =>
        !string.IsNullOrEmpty(a)
        && !string.IsNullOrEmpty(b)
        && string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
