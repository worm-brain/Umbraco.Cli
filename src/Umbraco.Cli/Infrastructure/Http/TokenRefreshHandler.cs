using System.Net;
using System.Net.Http.Headers;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Retries a request once with a new token when the server answers 401 (#248). With tokens cached
/// on disk between runs, a cached token can be rejected before it expires - the API user was
/// revoked or the server's signing keys changed - and the run should recover rather than fail.
/// <para>
/// It retries the one rejected <b>request</b>, not the command: re-running a whole
/// <c>content apply</c> after a mid-run 401 would repeat the writes that had succeeded. A request
/// body is buffered first so it can be sent twice. Only one refresh happens per run; a 401 with the
/// new token is returned as it is, because then the credentials really are refused.
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
        if (state.Refresh is not { } refresh)
            return await base.SendAsync(request, ct);

        // After a refresh the client's default header still holds the rejected token.
        if (state.Token is { } replaced)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replaced);
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(ct);

        var sentWith = request.Headers.Authorization?.Parameter;
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        await state.Gate.WaitAsync(ct);
        try
        {
            // Another request may have refreshed while this one waited; then just use its token.
            if ((state.Token is null || state.Token == sentWith) && !state.Refreshed)
            {
                state.Refreshed = true;
                state.Token = await refresh(ct);
            }
        }
        catch (UmbracoAuthException)
        {
            // The credentials themselves are refused now; the original 401 says so.
            return response;
        }
        finally
        {
            state.Gate.Release();
        }

        if (state.Token is not { } fresh || fresh == sentWith)
            return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
        return await base.SendAsync(request, ct);
    }
}
