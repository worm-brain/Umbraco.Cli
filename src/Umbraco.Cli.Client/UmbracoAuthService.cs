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
