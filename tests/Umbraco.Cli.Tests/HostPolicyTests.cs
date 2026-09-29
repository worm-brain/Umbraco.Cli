using System.CommandLine;
using System.Net;
using System.Text;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Where the CLI may send credentials: never over plain HTTP to a remote host, and a configured
/// client secret only to the host it was configured for (<see cref="HostPolicy"/>, applied by
/// <see cref="UmbracoAuthService"/>, <see cref="CommandContextFactory"/> and <c>auth doctor</c>).
/// </summary>
[Collection("ConsoleCapture")]
public class HostPolicyTests
{
    /// <summary>Issues a token for every request and records where each request went.</summary>
    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"t","expires_in":299,"token_type":"Bearer"}""",
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

    /// <summary>What one <see cref="CommandContextFactory.CreateAsync"/> run did.</summary>
    private sealed record Run(bool Aborted, IReadOnlyList<Uri> Requests, string Stderr);

    /// <summary>
    /// Builds a context against a profile stored for <paramref name="configHost"/> (with a client
    /// secret), parsing <paramref name="args"/>, and reports whether it aborted and what was sent.
    /// </summary>
    private static async Task<Run> CreateWith(string configHost, string args)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json");
        new ConfigStore(configPath).Save(
            new CliConfig
            {
                Host = configHost,
                ClientId = "id",
                ClientSecret = "stored-secret",
            }
        );
        var endpoint = new TokenEndpoint();
        var http = new Factory(endpoint);
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(configPath),
            new UmbracoAuthService(http),
            http,
            global,
            new ClientFactory(),
            new MutationInterceptState()
        );
        var root = new RootCommand();
        global.AddTo(root);

        var original = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var ex = await Record.ExceptionAsync(() => factory.CreateAsync(root.Parse(args)));
            return new Run(ex is CommandAbortedException, endpoint.Requests, stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
            File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void InsecureTransportError_HttpsOrLoopback_IsNull(string host)
    {
        var error = HostPolicy.InsecureTransportError(host);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("HTTP://intranet:8080/")]
    public void InsecureTransportError_HttpRemoteHost_ReturnsError(string host)
    {
        var error = HostPolicy.InsecureTransportError(host);

        Assert.Contains("plain HTTP", error);
    }

    [Theory]
    [InlineData("https://site.test", "https://SITE.test/", true)]
    [InlineData("https://site.test", "https://other.test", false)]
    [InlineData("https://site.test", "http://site.test", false)]
    [InlineData("https://site.test", null, false)]
    public void IsSameHost_Pairs_ComparesNormalised(string a, string? b, bool expected)
    {
        var same = HostPolicy.IsSameHost(a, b);

        Assert.Equal(expected, same);
    }

    [Fact]
    public async Task GetTokenAsync_HttpNonLoopbackHost_ThrowsWithoutSendingTheSecret()
    {
        var endpoint = new TokenEndpoint();
        var service = new UmbracoAuthService(new Factory(endpoint));

        var ex = await Record.ExceptionAsync(() =>
            service.GetTokenAsync("http://example.com", "id", "secret")
        );

        Assert.Equal((true, 0), (ex is UmbracoAuthException, endpoint.Requests.Count));
    }

    [Fact]
    public async Task CreateAsync_HostOverrideDiffersFromConfig_DoesNotSendStoredSecret()
    {
        var run = await CreateWith("https://site.test", "--host https://other.test --output json");

        Assert.Equal((true, 0), (run.Aborted, run.Requests.Count));
    }

    [Fact]
    public async Task CreateAsync_HostOverrideDiffersFromConfig_ExplainsHowToTargetThatHost()
    {
        var run = await CreateWith("https://site.test", "--host https://other.test --output json");

        Assert.Contains("--token", run.Stderr);
    }

    [Fact]
    public async Task CreateAsync_HostOverrideSameAsConfig_ExchangesWithThatHost()
    {
        var run = await CreateWith("https://site.test", "--host https://SITE.test/ --output json");

        Assert.Equal("site.test", Assert.Single(run.Requests).Host);
    }

    [Fact]
    public async Task CreateAsync_HostOverrideWithToken_RunsWithoutTheStoredSecret()
    {
        var run = await CreateWith(
            "https://site.test",
            "--host https://other.test --token given --output json"
        );

        Assert.Equal((false, 0), (run.Aborted, run.Requests.Count));
    }

    [Fact]
    public async Task CreateAsync_HttpNonLoopbackHost_AbortsWithError()
    {
        var run = await CreateWith("http://site.test", "--output json");

        Assert.Equal((true, 0), (run.Aborted, run.Requests.Count));
    }

    [Fact]
    public async Task CreateAsync_HttpNonLoopbackHostWithToken_Aborts()
    {
        // A bearer token is as much a credential as the secret, so --token does not lift the rule.
        var run = await CreateWith(
            "https://site.test",
            "--host http://other.test --token given --output json"
        );

        Assert.True(run.Aborted);
    }

    [Fact]
    public async Task CreateAsync_HttpLoopbackHost_ExchangesCredentials()
    {
        var run = await CreateWith("http://localhost:5000", "--output json");

        Assert.Single(run.Requests);
    }

    [Fact]
    public async Task RunChecksAsync_HttpNonLoopbackHost_FailsTheHostCheck()
    {
        var endpoint = new TokenEndpoint();
        var http = new Factory(endpoint);

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "http://example.com",
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: http,
            authService: new UmbracoAuthService(http),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        var only = Assert.Single(checks);
        Assert.Equal(("Host configured", "fail"), (only.Check, only.Status));
    }

    [Fact]
    public async Task RunChecksAsync_CredentialsForAnotherHost_NeverPostsThem()
    {
        var endpoint = new TokenEndpoint();
        var http = new Factory(endpoint);

        await AuthDoctorCommand.RunChecksAsync(
            host: "https://other.test",
            tokenOverride: null,
            config: new CliConfig
            {
                Host = "https://site.test",
                ClientId = "id",
                ClientSecret = "stored-secret",
            },
            httpClientFactory: http,
            authService: new UmbracoAuthService(http),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        Assert.DoesNotContain(endpoint.Requests, u => u.AbsolutePath.EndsWith("/token"));
    }

    [Fact]
    public async Task RunChecksAsync_CredentialsForAnotherHost_FailsTheCredentialsCheck()
    {
        var endpoint = new TokenEndpoint();
        var http = new Factory(endpoint);

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://other.test",
            tokenOverride: null,
            config: new CliConfig
            {
                Host = "https://site.test",
                ClientId = "id",
                ClientSecret = "stored-secret",
            },
            httpClientFactory: http,
            authService: new UmbracoAuthService(http),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        Assert.Equal("fail", checks.Single(c => c.Check == "Credentials present").Status);
    }
}
