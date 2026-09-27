namespace Umbraco.Cli.Client;

/// <summary>
/// Somewhere a client-credentials token outlives the process that fetched it (#248), so a script
/// running twenty commands exchanges credentials once rather than twenty times. Every CLI run is
/// a new process, so without one the in-memory cache in <see cref="UmbracoAuthService"/> never
/// gets a second use.
/// <para>
/// Keys are opaque strings built by <see cref="UmbracoAuthService"/> (host, client id and a
/// fingerprint of the secret, never the secret itself). An implementation must fail soft: a cache
/// it cannot read is an empty cache, and a write it cannot make is dropped, because a broken cache
/// must never stop a command that could simply authenticate.
/// </para>
/// </summary>
public interface ITokenCache
{
    /// <summary>The cached token for <paramref name="key"/>, or null when there is none.</summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The entry, whether or not it is still fresh; the caller checks.</returns>
    CachedToken? Read(string key);

    /// <summary>Stores <paramref name="token"/> under <paramref name="key"/>, replacing any entry.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">The token and when it stops being reused.</param>
    void Write(string key, CachedToken token);

    /// <summary>Removes the entry for <paramref name="key"/>, if any.</summary>
    /// <param name="key">The cache key.</param>
    void Remove(string key);

    /// <summary>
    /// Removes every entry whose key starts with <paramref name="prefix"/> (ordinal). Logout uses it
    /// to drop a profile's tokens whatever secret they were fetched with (#260).
    /// </summary>
    /// <param name="prefix">The key prefix.</param>
    void RemoveByPrefix(string prefix);
}

/// <summary>A cached bearer token.</summary>
/// <param name="AccessToken">The bearer token.</param>
/// <param name="RefreshAt">When it stops being reused: shortly before it expires.</param>
public sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAt);
