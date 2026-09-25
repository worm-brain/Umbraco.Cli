using System.Net;
using System.Net.Http.Headers;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A request rejected with 401 is retried once with a new token (#248), because a cached token can
/// be refused before it expires; a second 401, or a token given with <c>--token</c>, is final.
/// </summary>
public class TokenRefreshHandlerTests
{
    /// <summary>Accepts only <paramref name="validToken"/>; records what each attempt carried.</summary>
    private sealed class Server(string validToken) : HttpMessageHandler
    {
        public List<(string? Token, string? Body)> Attempts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var token = request.Headers.Authorization?.Parameter;
            Attempts.Add((token, body));
            return new HttpResponseMessage(
                token == validToken ? HttpStatusCode.OK : HttpStatusCode.Unauthorized
            );
        }
    }

    private static HttpClient Client(Server server, TokenRefreshState state)
    {
        var client = new HttpClient(new TokenRefreshHandler(state) { InnerHandler = server })
        {
            BaseAddress = new Uri("https://x/"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "stale"
        );
        return client;
    }

    private static TokenRefreshState Refreshing(string to, Action? onRefresh = null)
    {
        var state = new TokenRefreshState();
        state.Reset(_ =>
        {
            onRefresh?.Invoke();
            return Task.FromResult(to);
        });
        return state;
    }

    [Fact]
    public async Task Send_RejectedCachedToken_RetriesWithAFreshOneAndTheSameBody()
    {
        var server = new Server(validToken: "fresh");
        var client = Client(server, Refreshing("fresh"));

        var response = await client.PostAsync("doc", new StringContent("""{"a":1}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("stale", """{"a":1}"""), ("fresh", """{"a":1}""")], server.Attempts);
    }

    [Fact]
    public async Task Send_AfterARefresh_LaterRequestsUseTheNewToken()
    {
        var server = new Server(validToken: "fresh");
        var client = Client(server, Refreshing("fresh"));
        await client.GetAsync("one");

        await client.GetAsync("two");

        Assert.Equal("fresh", server.Attempts[^1].Token);
        Assert.Equal(3, server.Attempts.Count); // stale, fresh, then fresh straight away
    }

    [Fact]
    public async Task Send_NewTokenAlsoRejected_ReturnsThe401WithoutLooping()
    {
        var server = new Server(validToken: "never");
        var refreshes = 0;
        var client = Client(server, Refreshing("fresh", () => refreshes++));

        var first = await client.GetAsync("one");
        var second = await client.GetAsync("two");

        Assert.Equal(
            (HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, 1),
            (first.StatusCode, second.StatusCode, refreshes)
        );
    }

    [Fact]
    public async Task Send_TokenGivenWithTokenFlag_IsNotRefreshed()
    {
        var server = new Server(validToken: "fresh");
        var state = new TokenRefreshState();
        state.Reset(null);

        var response = await Client(server, state).GetAsync("doc");

        Assert.Equal(
            (HttpStatusCode.Unauthorized, 1),
            (response.StatusCode, server.Attempts.Count)
        );
    }

    [Fact]
    public async Task Send_RefreshItselfRefused_ReturnsTheOriginal401()
    {
        var server = new Server(validToken: "fresh");
        var state = new TokenRefreshState();
        state.Reset(_ => throw new UmbracoAuthException(401, "invalid_client"));

        var response = await Client(server, state).GetAsync("doc");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
