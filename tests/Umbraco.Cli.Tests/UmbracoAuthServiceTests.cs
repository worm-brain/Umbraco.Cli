using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests that the token fetch converts transport failures into <see cref="UmbracoAuthException"/>
/// rather than letting a raw <see cref="HttpRequestException"/> escape and crash the process
/// (issue #81), and that a cached token is reused for most of its lifetime (#251).
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

    /// <summary>Issues a token with the given lifetime on every request, and counts the requests.</summary>
    private sealed class CountingTokenHandler(int expiresIn) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests++;
            var body =
                $$"""{"access_token":"token-{{Requests}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""";
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        body,
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// Asks for a token, moves the clock on, asks again, and returns how many token requests went
    /// out in total.
    /// </summary>
    private static async Task<int> TokenRequestsAfter(int expiresIn, TimeSpan elapsed)
    {
        var handler = new CountingTokenHandler(expiresIn);
        var clock = new ManualClock();
        var service = new UmbracoAuthService(
            new SingleClientFactory(new HttpClient(handler)),
            clock
        );

        await service.GetTokenAsync("https://x", "client", "secret", CancellationToken.None);
        clock.Advance(elapsed);
        await service.GetTokenAsync("https://x", "client", "secret", CancellationToken.None);

        return handler.Requests;
    }

    [Fact]
    public async Task GetTokenAsync_UmbracoLifetimeWellBeforeExpiry_ReusesCachedToken()
    {
        // #251: Umbraco issues 299 s tokens. The old fixed five-minute margin was longer than
        // that, so the token was never reused and every request re-authenticated.
        var requests = await TokenRequestsAfter(expiresIn: 299, TimeSpan.FromSeconds(200));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetTokenAsync_UmbracoLifetimeInsideRefreshMargin_RequestsNewToken()
    {
        // A 299 s token's margin is a tenth of its lifetime (29.9 s), so at 280 s it is refreshed.
        var requests = await TokenRequestsAfter(expiresIn: 299, TimeSpan.FromSeconds(280));

        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task GetTokenAsync_LongLifetimeWithinOneMinuteOfExpiry_RequestsNewToken()
    {
        // The margin is capped at 60 s, so an hour-long token is refreshed in its last minute.
        var requests = await TokenRequestsAfter(expiresIn: 3600, TimeSpan.FromSeconds(3550));

        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task GetTokenAsync_LongLifetimeBeforeTheLastMinute_ReusesCachedToken()
    {
        var requests = await TokenRequestsAfter(expiresIn: 3600, TimeSpan.FromSeconds(3500));

        Assert.Equal(1, requests);
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
