using System.CommandLine;
using System.Net;
using System.Text;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>auth login</c> saves the credentials only once the token endpoint has issued a token, and
/// reports a failed exchange the way every other command does (#445): no response is
/// <c>unreachable</c> or <c>timeout</c> with exit 1, a refusal is <c>not_authenticated</c> with exit 2.
/// </summary>
[Collection("ConsoleCapture")]
public class LoginCommandTests
{
    /// <summary>A token endpoint whose every answer (or transport failure) comes from <c>answer</c>.</summary>
    private sealed class TokenEndpointThat(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(answer());
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>What one <c>auth login</c> run left behind.</summary>
    /// <param name="Exit">The exit code.</param>
    /// <param name="Stderr">Everything written to stderr (the JSON error, on failure).</param>
    /// <param name="Saved">Whether the config file was written.</param>
    private sealed record LoginRun(int Exit, string Stderr, bool Saved)
    {
        /// <summary>The error envelope's <c>category</c>.</summary>
        public string? Category =>
            JsonDocument.Parse(Stderr).RootElement.GetProperty("category").GetString();
    }

    /// <summary>
    /// Runs <c>auth login</c> for <c>https://site.test</c> against a token endpoint that behaves as
    /// <paramref name="answer"/>, with a config file of its own.
    /// </summary>
    /// <param name="answer">The token endpoint's response, or a throw for a transport failure.</param>
    /// <param name="host">The host to log in to.</param>
    /// <returns>The run's outcome.</returns>
    private static async Task<LoginRun> Login(
        Func<HttpResponseMessage> answer,
        string host = "https://site.test"
    )
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json");
        var global = new GlobalOptions();
        var root = new RootCommand();
        global.AddTo(root);
        var auth = new Command("auth");
        auth.Add(
            LoginCommand.Build(
                global,
                new ConfigStore(configPath),
                new UmbracoAuthService(new Factory(new TokenEndpointThat(answer)))
            )
        );
        root.Add(auth);
        var (originalOut, originalError) = (Console.Out, Console.Error);
        var stderr = new StringWriter();
        Console.SetOut(new StringWriter());
        Console.SetError(stderr);

        try
        {
            var exit = await root.Parse(
                    $"auth login --host {host} --client-id id --client-secret secret --output json"
                )
                .InvokeAsync();
            return new LoginRun(exit, stderr.ToString(), File.Exists(configPath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Login_TokenIssued_SavesTheCredentialsAndExitsZero()
    {
        var run = await Login(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"t","expires_in":299,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        Assert.Equal((0, true), (run.Exit, run.Saved));
    }

    [Fact]
    public async Task Login_HostUnreachable_ReportsUnreachableWithExitOneAndSavesNothing()
    {
        var run = await Login(() => throw new HttpRequestException("connection refused"));

        Assert.Equal((1, "unreachable", false), (run.Exit, run.Category, run.Saved));
    }

    [Fact]
    public async Task Login_CredentialsRefused_ReportsNotAuthenticatedWithExitTwo()
    {
        var run = await Login(() =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid_client"}"""),
            }
        );

        Assert.Equal((2, "not_authenticated", false), (run.Exit, run.Category, run.Saved));
    }

    [Fact]
    public async Task Login_TokenEndpointAnswers500_ReportsServerErrorWithExitOneAndSavesNothing()
    {
        // #449: the server failed; the credentials were never judged.
        var run = await Login(() =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("oops"),
            }
        );

        Assert.Equal((1, "server_error", false), (run.Exit, run.Category, run.Saved));
    }

    [Fact]
    public async Task Login_PlainHttpRemoteHost_ReportsRefusedWithExitTwoAndSavesNothing()
    {
        // #450: the same refusal every other command gives, and nothing reaches the endpoint.
        var run = await Login(
            () => throw new InvalidOperationException("must not be sent"),
            host: "http://example.com"
        );

        Assert.Equal((2, "refused", false), (run.Exit, run.Category, run.Saved));
    }
}
