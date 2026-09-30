using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Umbraco.Cli.Client;

/// <summary>
/// Fetches and caches OAuth2 Client Credentials tokens from the Umbraco
/// back-office token endpoint.  Thread-safe; refreshes transparently.
/// <para>
/// Tokens are cached per host, client id and secret: in memory, and in an optional
/// <see cref="ITokenCache"/> that outlives the process (#248). The secret is part of the key as a
/// SHA-256 fingerprint, so a changed or mistyped secret never picks up a token issued to the old
/// one - it has to authenticate, and fail, like it would without a cache.
/// </para>
/// </summary>
public sealed class UmbracoAuthService
{
    /// <summary>The largest refresh margin, for tokens with a long lifetime.</summary>
    private static readonly TimeSpan MaxRefreshMargin = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _time;
    private readonly ITokenCache? _cache;

    // In-memory tokens by cache key. RefreshAt is worked out once, when the token arrives, so the
    // margin can be sized against that token's own lifetime (#251).
    private readonly Dictionary<string, CachedToken> _tokens = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Creates the service.</summary>
    /// <param name="httpClientFactory">Creates the client used for the token request.</param>
    /// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when omitted. Tests pass a fake.</param>
    /// <param name="cache">Where tokens outlive the process (#248); null keeps them in memory only.</param>
    public UmbracoAuthService(
        IHttpClientFactory httpClientFactory,
        TimeProvider? timeProvider = null,
        ITokenCache? cache = null
    )
    {
        _httpClientFactory = httpClientFactory;
        _time = timeProvider ?? TimeProvider.System;
        _cache = cache;
    }

    /// <summary>
    /// The cache key for a set of credentials: the host (without a trailing slash, lower-cased),
    /// the client id, and the first 16 bytes of the secret's SHA-256 in hex. The secret itself is
    /// never part of anything written to disk.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    /// <param name="clientSecret">The API user's client secret.</param>
    /// <returns>The key.</returns>
    internal static string CacheKey(string host, string clientId, string clientSecret)
    {
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret)),
            0,
            16
        );
        return $"{CacheKeyPrefix(host, clientId)}{fingerprint}";
    }

    /// <summary>The part of <see cref="CacheKey"/> shared by every secret for one host and client id.</summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    /// <returns>The key prefix, ending in the separator so one client id is not a prefix of another.</returns>
    internal static string CacheKeyPrefix(string host, string clientId) =>
        $"{host.TrimEnd('/').ToLowerInvariant()}|{clientId}|";

    /// <summary>
    /// Drops every cached token for a host and client id, in memory and in the persistent cache,
    /// whichever secret fetched it (#260). <c>auth logout</c> calls this so that no usable bearer
    /// token for the profile is left on disk.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    public void ForgetCachedTokens(string host, string clientId)
    {
        var prefix = CacheKeyPrefix(host, clientId);
        _lock.Wait();
        try
        {
            foreach (
                var key in _tokens
                    .Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                    .ToList()
            )
                _tokens.Remove(key);
            _cache?.RemoveByPrefix(prefix);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// How long before expiry a token is refreshed: a tenth of its lifetime, capped at
    /// <see cref="MaxRefreshMargin"/>.
    /// <para>
    /// A fixed margin breaks on short-lived tokens. Umbraco issues client-credentials tokens
    /// for 299 s, and the old fixed five-minute margin was longer than that, so every token
    /// was already "stale" when it arrived and every request re-authenticated (#251).
    /// </para>
    /// </summary>
    /// <param name="lifetime">The token's lifetime (<c>expires_in</c>).</param>
    /// <returns>The margin, always shorter than <paramref name="lifetime"/> when it is positive.</returns>
    private static TimeSpan RefreshMargin(TimeSpan lifetime)
    {
        var tenth = lifetime / 10;
        return tenth < MaxRefreshMargin ? tenth : MaxRefreshMargin;
    }

    /// <summary>
    /// Returns a valid bearer token. A cached token - in memory, then in the
    /// <see cref="ITokenCache"/> - is reused until shortly before it expires (see
    /// <see cref="RefreshMargin"/>); after that a new one is requested and cached.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    /// <param name="clientSecret">The API user's client secret.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="fresh">
    /// Skip both caches and exchange the credentials, for callers whose point is to test them
    /// (<c>auth login</c>, <c>auth doctor</c>). The new token is still cached.
    /// </param>
    /// <returns>The bearer token.</returns>
    /// <exception cref="UmbracoAuthException">The host is plain <c>http://</c> and not loopback (see <see cref="HostPolicy.InsecureTransportError"/>), or the token request failed, timed out, could not reach the host, or returned an unreadable body; <see cref="UmbracoAuthException.Category"/> says which kind.</exception>
    public async Task<string> GetTokenAsync(
        string host,
        string clientId,
        string clientSecret,
        CancellationToken ct = default,
        bool fresh = false
    )
    {
        // Never put the client secret on the wire in cleartext. Checked here, not only by the
        // callers, so every path that exchanges credentials (commands, login, doctor) is covered.
        if (HostPolicy.InsecureTransportError(host) is { } insecure)
            throw new UmbracoAuthException(0, insecure);

        var key = CacheKey(host, clientId, clientSecret);
        await _lock.WaitAsync(ct);
        try
        {
            if (!fresh && Reusable(key) is { } reusable)
                return reusable;

            var http = _httpClientFactory.CreateClient();
            var tokenUrl =
                $"{host.TrimEnd('/')}/umbraco/management/api/v1/security/back-office/token";

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            };

            // Convert transport-level failures (host unreachable, timeout, unreadable body)
            // into UmbracoAuthException so the command layer reports a clean error and a
            // non-zero exit rather than crashing with a raw stack trace (issue #81). This
            // mirrors the "errors are data" contract the rest of the client already follows. A
            // genuine caller cancellation (ct signalled) is left to propagate.
            try
            {
                using var response = await http.PostAsync(
                    tokenUrl,
                    new FormUrlEncodedContent(form),
                    ct
                );

                if (!response.IsSuccessStatusCode)
                {
                    // Never echo the raw body: a server or proxy that reflects the form would
                    // put the client secret into the error output (SEC-AUTH-001).
                    var body = await response.Content.ReadAsStringAsync(ct);
                    throw new UmbracoAuthException(
                        (int)response.StatusCode,
                        DescribeTokenFailure((int)response.StatusCode, body, clientSecret)
                    );
                }

                var token =
                    await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new UmbracoAuthException(0, "Empty token response");

                var lifetime = TimeSpan.FromSeconds(token.ExpiresIn);
                var cached = new CachedToken(
                    token.AccessToken,
                    _time.GetUtcNow() + lifetime - RefreshMargin(lifetime)
                );
                _tokens[key] = cached;
                _cache?.Write(key, cached);
                return cached.AccessToken;
            }
            // No response at all is classified as the request guard classifies any other request
            // (UmbracoManagementClient.GuardedApiAsync): unreachable or timeout, not a refusal of
            // the credentials (#445). The transport exception is kept as the inner exception so
            // the 401 retry can rethrow it as the request's own failure.
            catch (HttpRequestException ex)
            {
                throw new UmbracoAuthException(
                    0,
                    $"Could not reach the Umbraco instance at {host} to authenticate: {ex.Message}",
                    FailureCategory.Unreachable,
                    ex
                );
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // A timeout surfaces as a cancellation whose token is NOT the caller's.
                throw new UmbracoAuthException(
                    0,
                    $"The authentication request to the Umbraco instance at {host} timed out.",
                    FailureCategory.Timeout,
                    ex
                );
            }
            catch (Exception ex)
                when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                // JsonException: malformed body. NotSupportedException: an unexpected content
                // type (e.g. a token endpoint returning text/html on error) that ReadFromJsonAsync
                // cannot deserialize — both must surface as a clean auth error, not a raw crash.
                throw new UmbracoAuthException(
                    0,
                    $"The Umbraco instance returned an unreadable token response: {ex.Message}"
                );
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The most characters of a server-supplied OAuth error field that are reported.</summary>
    private const int MaxErrorFieldLength = 200;

    /// <summary>
    /// The message for a failed token request. Only the standard OAuth <c>error</c> and
    /// <c>error_description</c> fields of a JSON body are reported (RFC 6749 section 5.2), each
    /// with the client secret removed, control and format characters stripped, and cut to
    /// <see cref="MaxErrorFieldLength"/> characters. Nothing else in the body is ever shown, so a
    /// server that reflects the request cannot make the CLI print the secret it just sent.
    /// </summary>
    /// <param name="statusCode">The HTTP status of the token response.</param>
    /// <param name="body">The raw response body; may be empty, non-JSON or hostile.</param>
    /// <param name="clientSecret">The secret that was sent, scrubbed from anything reported.</param>
    /// <returns>A single-line message naming the status and, when present, the OAuth error.</returns>
    internal static string DescribeTokenFailure(int statusCode, string body, string clientSecret)
    {
        string? error = null;
        string? description = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                error = OAuthField(doc.RootElement, "error", clientSecret);
                description = OAuthField(doc.RootElement, "error_description", clientSecret);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON (an HTML error page, plain text): nothing in it is safe to quote.
        }

        var prefix = $"Token request failed ({statusCode})";
        return (error, description) switch
        {
            (not null, not null) => $"{prefix}: {error} - {description}",
            (not null, null) => $"{prefix}: {error}",
            (null, not null) => $"{prefix}: {description}",
            _ => $"{prefix}: the server returned an unexpected error response.",
        };
    }

    /// <summary>
    /// One string field of an OAuth error body, made safe to print: the client secret replaced by
    /// <c>[redacted]</c>, control characters turned into spaces, format characters (bidi
    /// overrides, zero-width marks) dropped, and the result trimmed and length-capped.
    /// </summary>
    /// <param name="root">The body's root object.</param>
    /// <param name="name">The field name.</param>
    /// <param name="clientSecret">The secret that was sent.</param>
    /// <returns>The cleaned value, or null when the field is missing, not a string, or empty once cleaned.</returns>
    private static string? OAuthField(
        System.Text.Json.JsonElement root,
        string name,
        string clientSecret
    )
    {
        if (
            !root.TryGetProperty(name, out var value)
            || value.ValueKind != System.Text.Json.JsonValueKind.String
        )
            return null;

        var text = value.GetString() ?? "";
        // Scrub before cutting, so a cut can never leave a prefix of the secret behind.
        if (clientSecret.Length > 0)
            text = text.Replace(clientSecret, "[redacted]", StringComparison.Ordinal);

        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c))
                clean.Append(' ');
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
                clean.Append(c);
        }

        var result = clean.ToString().Trim();
        if (result.Length > MaxErrorFieldLength)
            result = result[..MaxErrorFieldLength].TrimEnd() + "...";
        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// A still-fresh token for <paramref name="key"/>: the in-memory one, else the persistent
    /// cache's (which is then kept in memory too). Null when neither has a fresh one.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The token, or null.</returns>
    private string? Reusable(string key)
    {
        var now = _time.GetUtcNow();
        if (_tokens.TryGetValue(key, out var inMemory) && now < inMemory.RefreshAt)
            return inMemory.AccessToken;
        if (_cache?.Read(key) is { } persisted && now < persisted.RefreshAt)
        {
            _tokens[key] = persisted;
            return persisted.AccessToken;
        }
        return null;
    }

    /// <summary>
    /// Drops the cached token for these credentials, in memory and in the persistent cache, so the
    /// next <see cref="GetTokenAsync"/> requests a new one. Used when the server rejects a cached
    /// token with a 401 (#248) - it was revoked, or the server's signing keys changed.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    /// <param name="clientSecret">The API user's client secret.</param>
    public void Invalidate(string host, string clientId, string clientSecret)
    {
        var key = CacheKey(host, clientId, clientSecret);
        _lock.Wait();
        try
        {
            _tokens.Remove(key);
            _cache?.Remove(key);
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// A client-credentials token could not be obtained. <see cref="Category"/> tells a caller whether
/// to fix the credentials or to wait for the site (#445).
/// </summary>
/// <param name="statusCode">The token endpoint's HTTP status; 0 when there is none.</param>
/// <param name="message">A single-line message that never contains the client secret.</param>
/// <param name="category">Why the exchange failed; a refusal of the credentials when omitted.</param>
/// <param name="innerException">
/// The transport failure (<see cref="HttpRequestException"/>, or the timeout's
/// <see cref="OperationCanceledException"/>) behind an unreachable or timed-out exchange; null otherwise.
/// </param>
public sealed class UmbracoAuthException(
    int statusCode,
    string message,
    FailureCategory category = FailureCategory.NotAuthenticated,
    Exception? innerException = null
) : Exception(message, innerException)
{
    /// <summary>The token endpoint's HTTP status, or 0 when there was no status to report (never sent, never answered, or an unreadable success).</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>
    /// <see cref="FailureCategory.Unreachable"/> or <see cref="FailureCategory.Timeout"/> when the
    /// token request got no response, the categories any other request without one gets;
    /// otherwise <see cref="FailureCategory.NotAuthenticated"/>: the token endpoint answered with an
    /// error or an unreadable body, or a plain-HTTP host was refused before anything was sent.
    /// </summary>
    public FailureCategory Category { get; } = category;
}
