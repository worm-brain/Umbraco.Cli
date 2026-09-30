using System.Net;
using System.Net.Http.Headers;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Sends each request with the run's current token, and retries it once with a new one when the
/// server answers 401 (#248). With tokens cached on disk between runs, a cached token can be
/// rejected before it expires - the API user was revoked or the server's signing keys changed -
/// and the run should recover rather than fail.
/// <para>
/// It retries the one rejected <b>request</b>, not the command: re-running a whole
/// <c>content apply</c> after a mid-run 401 would repeat the writes that had succeeded. A request
/// body is buffered first so it can be sent twice. <see cref="TokenRefreshState"/> decides whether
/// there is a token to retry with.
/// </para>
/// <para>
/// A renewal that gets no response fails the request with that transport failure rather than
/// returning the 401 (#445), so the command reports <c>unreachable</c> or <c>timeout</c>, as for
/// any other request that never reached the server.
/// </para>
/// </summary>
public sealed class TokenRefreshHandler(TokenRefreshState state) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        // The state, not the client's default header, holds the current token: after a renewal
        // the default header is stale.
        if (state.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!state.CanRenew)
            return await base.SendAsync(request, ct);

        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(ct);
        var sentWith = request.Headers.Authorization?.Parameter;
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        string? fresh;
        try
        {
            fresh = await state.RenewAsync(sentWith, ct);
        }
        catch
        {
            // The renewal's failure replaces the 401 as the request's outcome; release the 401.
            response.Dispose();
            throw;
        }
        if (fresh is null || fresh == sentWith)
            return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
        return await base.SendAsync(request, ct);
    }
}
