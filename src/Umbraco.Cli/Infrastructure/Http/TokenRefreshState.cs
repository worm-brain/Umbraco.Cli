namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Per-invocation state for <see cref="TokenRefreshHandler"/> (#248): how to get a new token when
/// the server rejects the current one, and the token that replaced it. Set by
/// <c>CommandContextFactory</c> on every run, like <see cref="MutationInterceptState"/>, so a
/// reused state never carries the last run's credentials.
/// </summary>
public sealed class TokenRefreshState
{
    /// <summary>
    /// Serialises refreshes, so concurrent requests that all get a 401 cause one token request,
    /// and the rest retry with its result.
    /// </summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    /// Drops the rejected token and fetches a new one; null when the token was given with
    /// <c>--token</c>, which the CLI has no way to renew.
    /// </summary>
    public Func<CancellationToken, Task<string>>? Refresh { get; private set; }

    /// <summary>The token that replaced the rejected one, once a refresh has happened.</summary>
    internal string? Token { get; set; }

    /// <summary>Whether this run has refreshed already. Only once: a second 401 is a real one.</summary>
    internal bool Refreshed { get; set; }

    /// <summary>Starts a run: sets how to refresh (null for none) and forgets any earlier refresh.</summary>
    /// <param name="refresh">The refresh, or null when the token cannot be renewed.</param>
    public void Reset(Func<CancellationToken, Task<string>>? refresh)
    {
        Refresh = refresh;
        Token = null;
        Refreshed = false;
    }
}
