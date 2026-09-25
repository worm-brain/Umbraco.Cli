using System.CommandLine;
using System.Net;
using System.Text;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How <see cref="CommandContextFactory"/> arms the 401 refresh (#248): a token from client
/// credentials can be renewed, one given with <c>--token</c> cannot.
/// </summary>
[Collection("ConsoleCapture")]
public class CommandContextFactoryTests
{
    /// <summary>Issues a new token on every request and counts them.</summary>
    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"t{{Requests}}","expires_in":299,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ClientFactory : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) =>
            new FakeUmbracoManagementClient();
    }

    private static async Task<(TokenRefreshState State, TokenEndpoint Endpoint)> CreateWith(
        string args
    )
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json");
        new ConfigStore(configPath).Save(
            new CliConfig
            {
                Host = "https://site.test",
                ClientId = "umbraco-back-office-ci",
                ClientSecret = "secret",
            }
        );
        var endpoint = new TokenEndpoint();
        var http = new Factory(endpoint);
        var state = new TokenRefreshState();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(configPath),
            new UmbracoAuthService(http),
            http,
            global,
            new ClientFactory(),
            new MutationInterceptState(),
            state
        );
        var root = new RootCommand();
        global.AddTo(root);

        await factory.CreateAsync(root.Parse(args));
        File.Delete(configPath);
        return (state, endpoint);
    }

    [Fact]
    public async Task CreateAsync_ClientCredentials_ArmsARefreshThatExchangesThemAgain()
    {
        var (state, endpoint) = await CreateWith("--output json");

        var fresh = await state.RenewAsync("t1", CancellationToken.None);

        Assert.Equal(("t2", 2), (fresh, endpoint.Requests));
    }

    [Fact]
    public async Task CreateAsync_TokenFlag_HasNothingToRefreshWith()
    {
        var (state, _) = await CreateWith("--token given --output json");

        Assert.False(state.CanRenew);
    }
}
