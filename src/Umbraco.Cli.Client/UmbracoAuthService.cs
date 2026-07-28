using System.Net.Http.Json;

namespace Umbraco.Cli.Client;

/// <summary>
/// Fetches and caches OAuth2 Client Credentials tokens from the Umbraco
/// back-office token endpoint.  Thread-safe; refreshes transparently.
/// </summary>
public sealed class UmbracoAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UmbracoAuthService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Returns a valid bearer token, fetching/refreshing as needed.</summary>
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
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
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

                _cachedToken = token.AccessToken;
                _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
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

    public void Invalidate()
    {
        _lock.Wait();
        try
        {
            _cachedToken = null;
            _tokenExpiry = DateTimeOffset.MinValue;
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
