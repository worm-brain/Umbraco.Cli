using Umbraco.Cli.Client;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// The bearer token of the current invocation, and how to renew it when the server rejects it
/// (#248). <see cref="TokenRefreshHandler"/> stamps <see cref="Token"/> on every request, so this is
/// the one place that knows which token is current. Set by <c>CommandContextFactory</c> on every
/// run, like <see cref="MutationInterceptState"/>, so a reused state never carries the last run's
/// credentials.
/// </summary>
public sealed class TokenRefreshState
{
    // Serialises renewals, so concurrent requests that all get a 401 cause one token request and
    // the rest retry with its result.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Func<CancellationToken, Task<string>>? _refresh;
    private bool _renewed;

    /// <summary>The token requests carry, or null when no run has set one.</summary>
    public string? Token { get; private set; }

    /// <summary>Whether a rejected token can be renewed: false for one given with <c>--token</c>.</summary>
    public bool CanRenew => _refresh is not null;

    /// <summary>Starts a run with its token and how to renew it.</summary>
    /// <param name="token">The token to send.</param>
    /// <param name="refresh">
    /// Drops the rejected token and fetches a new one; null when the token was given with
    /// <c>--token</c>, which the CLI has no way to renew.
    /// </param>
    public void Reset(string token, Func<CancellationToken, Task<string>>? refresh)
    {
        Token = token;
        _refresh = refresh;
        _renewed = false;
    }

    /// <summary>
    /// The token to retry a request with after <paramref name="rejected"/> got a 401: the one
    /// another request already renewed to, or a new one. At most one renewal per run - a 401 on the
    /// renewed token means the credentials really are refused.
    /// </summary>
    /// <param name="rejected">The token the rejected request carried.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The token to retry with, or null when there is none (do not retry).</returns>
    public async Task<string?> RenewAsync(string? rejected, CancellationToken ct)
    {
        if (_refresh is not { } refresh)
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (Token != rejected)
                return Token;
            if (_renewed)
                return null;
            _renewed = true;
            Token = await refresh(ct);
            return Token;
        }
        catch (UmbracoAuthException)
        {
            // The credentials themselves are refused now; the original 401 stands.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
