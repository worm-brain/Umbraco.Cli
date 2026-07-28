using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests that the token fetch converts transport failures into <see cref="UmbracoAuthException"/>
/// rather than letting a raw <see cref="HttpRequestException"/> escape and crash the process
/// (issue #81).
/// </summary>
public class UmbracoAuthServiceTests
{
    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw ex;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>Returns a canned response body/status for every request (for parse-path tests).</summary>
    private sealed class StubHandler(string body, System.Net.HttpStatusCode status)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    /// <summary>Cancels the supplied source mid-request, then throws a token-carrying cancellation
    /// (as HttpClient does when the request is genuinely cancelled during the call).</summary>
    private sealed class CancellingHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }
    }

    private static UmbracoAuthService ServiceThatThrows(Exception ex) =>
        new(new SingleClientFactory(new HttpClient(new ThrowingHandler(ex))));

    [Fact]
    public async Task GetTokenAsync_HostUnreachable_ThrowsUmbracoAuthExceptionNotRaw()
    {
        // #81: connection-refused must surface as a clean UmbracoAuthException (which the
        // context factory renders as a clean error + exit 2), not a raw HttpRequestException.
        var service = ServiceThatThrows(new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<UmbracoAuthException>(() =>
            service.GetTokenAsync(
                "https://localhost:45000",
                "client",
                "secret",
                CancellationToken.None
            )
        );
        Assert.Contains("Could not reach", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_Timeout_ThrowsUmbracoAuthException()
    {
        // A timeout surfaces as a TaskCanceledException with no caller cancellation.
        var service = ServiceThatThrows(new TaskCanceledException("timeout"));

        var ex = await Assert.ThrowsAsync<UmbracoAuthException>(() =>
            service.GetTokenAsync("https://x", "client", "secret", CancellationToken.None)
        );
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_UnreadableBody_ThrowsUmbracoAuthException()
    {
        // #81: a 200 with a non-JSON / wrong-content-type body must surface as a clean auth
        // error, not a raw JsonException/NotSupportedException.
        var service = new UmbracoAuthService(
            new SingleClientFactory(
                new HttpClient(
                    new StubHandler("<html>not json</html>", System.Net.HttpStatusCode.OK)
                )
            )
        );

        var ex = await Assert.ThrowsAsync<UmbracoAuthException>(() =>
            service.GetTokenAsync("https://x", "client", "secret", CancellationToken.None)
        );
        Assert.Contains("unreadable", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_GenuineCancellationDuringRequest_Propagates()
    {
        // A real caller cancellation that fires mid-request must propagate as an
        // OperationCanceledException (exercising the `when (!ct.IsCancellationRequested)` filter,
        // which is false here), not be swallowed into an auth error.
        using var cts = new CancellationTokenSource();
        var service = new UmbracoAuthService(
            new SingleClientFactory(new HttpClient(new CancellingHandler(cts)))
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetTokenAsync("https://x", "client", "secret", cts.Token)
        );
    }
}
