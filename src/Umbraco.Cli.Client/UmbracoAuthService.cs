using System.Net.Http.Json;

namespace Umbraco.Cli.Client;

/// <summary>
/// Fetches and caches OAuth2 Client Credentials tokens from the Umbraco
/// back-office token endpoint.  Thread-safe; refreshes transparently.
/// </summary>
public sealed class UmbracoAuthService
{
    /// <summary>The largest refresh margin, for tokens with a long lifetime.</summary>
    private static readonly TimeSpan MaxRefreshMargin = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _time;
    private string? _cachedToken;

    // When the cached token stops being reused. Worked out once, when the token arrives, so the
    // margin can be sized against that token's own lifetime (#251).
    private DateTimeOffset _refreshAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Creates the service.</summary>
    /// <param name="httpClientFactory">Creates the client used for the token request.</param>
    /// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when omitted. Tests pass a fake.</param>
    public UmbracoAuthService(
        IHttpClientFactory httpClientFactory,
        TimeProvider? timeProvider = null
    )
    {
        _httpClientFactory = httpClientFactory;
        _time = timeProvider ?? TimeProvider.System;
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
    /// Returns a valid bearer token. A cached token is reused until shortly before it expires
    /// (see <see cref="RefreshMargin"/>); after that a new one is requested.
    /// </summary>
    /// <param name="host">The Umbraco base URL.</param>
    /// <param name="clientId">The API user's client id.</param>
    /// <param name="clientSecret">The API user's client secret.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bearer token.</returns>
    /// <exception cref="UmbracoAuthException">The token request failed, timed out, could not reach the host, or returned an unreadable body.</exception>
    public async Task<string> GetTokenAsync(
        string host,
        string clientId,
        string clientSecret,
        CancellationToken ct = default
    )
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && _time.GetUtcNow() < _refreshAt)
                return _cachedToken;

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
                    var body = await response.Content.ReadAsStringAsync(ct);
                    throw new UmbracoAuthException(
                        (int)response.StatusCode,
                        $"Token request failed ({(int)response.StatusCode}): {body}"
                    );
                }

                var token =
                    await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new UmbracoAuthException(0, "Empty token response");

                var lifetime = TimeSpan.FromSeconds(token.ExpiresIn);
                _cachedToken = token.AccessToken;
                _refreshAt = _time.GetUtcNow() + lifetime - RefreshMargin(lifetime);
                return _cachedToken;
            }
            catch (HttpRequestException ex)
            {
                throw new UmbracoAuthException(
                    0,
                    $"Could not reach the Umbraco instance at {host} to authenticate: {ex.Message}"
                );
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // A timeout surfaces as a cancellation whose token is NOT the caller's.
                throw new UmbracoAuthException(
                    0,
                    "The authentication request to the Umbraco instance timed out."
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

    /// <summary>Drops the cached token, so the next <see cref="GetTokenAsync"/> requests a new one.</summary>
    public void Invalidate()
    {
        _lock.Wait();
        try
        {
            _cachedToken = null;
            _refreshAt = DateTimeOffset.MinValue;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed class UmbracoAuthException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
