using System.CommandLine;
using System.Net;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>umbraco api</c> end to end (ADR 0010): the shipped tree, the real client and the real
/// <c>--dry-run</c> / <c>--readonly</c> interceptor, with only the transport replaced. The point of
/// the passthrough is that it keeps every guardrail a built-in command has, so each test runs a
/// command line and checks what reached the wire and what was printed.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class ApiCommandTests : IDisposable
{
    private const string Status = "/umbraco/management/api/v1/server/status";
    private const string Language = "/umbraco/management/api/v1/language";

    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-api-{Guid.NewGuid():N}.json"
    );
    private readonly string _bodyPath = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-api-body-{Guid.NewGuid():N}.json"
    );

    public ApiCommandTests()
    {
        // The guardrails read process env; a developer machine must not perturb these tests.
        foreach (
            var name in new[]
            {
                "UMBRACO_ALLOWED_COMMANDS",
                GlobalOptions.ReadOnlyVariable,
                GlobalOptions.DryRunVariable,
                GlobalOptions.OutputVariable,
            }
        )
            Environment.SetEnvironmentVariable(name, null);
        File.WriteAllText(_bodyPath, """{"isoCode":"da-DK","name":"Danish"}""");
    }

    public void Dispose()
    {
        File.Delete(_configPath);
        File.Delete(_bodyPath);
    }

    /// <summary>Hands out clients whose requests pass the dry-run/read-only interceptor on to <paramref name="handler"/>.</summary>
    private sealed class InterceptedHttp(RoutingHandler handler, MutationInterceptState state)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new MutationInterceptorHandler(state) { InnerHandler = handler });
    }

    /// <summary>The shipped tree over <paramref name="handler"/>, and its global options.</summary>
    private (RootCommand Root, GlobalOptions Global) Build(
        RoutingHandler handler,
        string? allowedCommands
    )
    {
        if (allowedCommands is not null)
            File.WriteAllText(_configPath, $$"""{"allowedCommands":"{{allowedCommands}}"}""");
        var state = new MutationInterceptState();
        var http = new InterceptedHttp(handler, state);
        var configStore = new ConfigStore(_configPath);
        var auth = new UmbracoAuthService(http);
        var global = new GlobalOptions();
        var clients = new UmbracoManagementClientFactory();
        var executor = new CommandExecutor(
            new CommandContextFactory(configStore, auth, http, global, clients, state),
            new ConsoleConfirmationPrompt()
        );
        return (CliRoot.Build(global, configStore, auth, executor, http, clients), global);
    }

    /// <summary>
    /// Runs <paramref name="commandLine"/> as <c>Program.cs</c> does: a parse error is reported
    /// through <see cref="ParseErrorReporter"/>, anything else is invoked.
    /// </summary>
    private async Task<(int Exit, string Stdout, string Stderr)> RunAsync(
        RoutingHandler handler,
        string commandLine,
        string? allowedCommands = null
    )
    {
        var (root, global) = Build(handler, allowedCommands);
        var parsed = root.Parse(
            $"--host https://site.example --token t --output json {commandLine}",
            CliParserConfiguration.Create()
        );

        var (origOut, origErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit =
                parsed.Errors.Count > 0
                    ? ParseErrorReporter.Report(parsed, global)
                    : await parsed.InvokeAsync();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static string? Category(string stderr) =>
        Json(stderr).GetProperty("category").GetString();

    [Fact]
    public async Task Get_Success_PrintsTheResponseBodyAsTheEnvelopesData()
    {
        var handler = Wire.Returning("""{"serverStatus":"Run"}""");

        var (exit, stdout, _) = await RunAsync(handler, $"api get {Status}");

        var envelope = Json(stdout);
        Assert.Equal(0, exit);
        Assert.Equal("Run", envelope.GetProperty("data").GetProperty("serverStatus").GetString());
        Assert.Equal("api.get", envelope.GetProperty("meta").GetProperty("command").GetString());
    }

    [Fact]
    public async Task Get_SendsOneGetToThePathOnTheHost()
    {
        var handler = Wire.Returning("{}");

        await RunAsync(handler, $"api get {Status}");

        var request = Assert.Single(handler.Recordings);
        Assert.Equal(
            (HttpMethod.Get, $"https://site.example{Status}"),
            (request.Method, request.Uri.AbsoluteUri)
        );
    }

    [Fact]
    public async Task Get_ErrorResponse_IsARejectedRequestCarryingUmbracosBody()
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.NotFound,
            """{"title":"The language could not be found","status":404}"""
        );

        var (exit, _, stderr) = await RunAsync(handler, $"api get {Language}/xx-XX");

        Assert.Equal((1, "request_rejected"), (exit, Category(stderr)));
        Assert.Equal(
            "The language could not be found",
            Json(stderr).GetProperty("details").GetProperty("title").GetString()
        );
    }

    [Fact]
    public async Task Post_WithABody_SendsTheBodyToThePath()
    {
        var handler = Wire.Blank();

        var (exit, _, _) = await RunAsync(handler, $"api post {Language} --json-body {_bodyPath}");

        Assert.Equal(0, exit);
        Assert.Equal(
            """{"isoCode":"da-DK","name":"Danish"}""",
            handler.RawBodyOf(HttpMethod.Post, "/language")
        );
    }

    [Fact]
    public async Task Post_UnderReadOnly_IsRefusedAndSendsNothing()
    {
        var handler = Wire.Blank();

        var (exit, _, stderr) = await RunAsync(
            handler,
            $"api post {Language} --json-body {_bodyPath} --readonly"
        );

        Assert.Equal((2, "readonly"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }

    [Fact]
    public async Task Post_UnderDryRun_PreviewsTheRequestAndSendsNothing()
    {
        var handler = Wire.Blank();

        var (exit, stdout, _) = await RunAsync(
            handler,
            $"api post {Language} --json-body {_bodyPath} --dry-run"
        );

        var envelope = Json(stdout);
        Assert.Equal(0, exit);
        Assert.Equal("dry-run", envelope.GetProperty("status").GetString());
        Assert.Equal("POST", envelope.GetProperty("data").GetProperty("method").GetString());
        Assert.Equal(
            "da-DK",
            envelope.GetProperty("data").GetProperty("body").GetProperty("isoCode").GetString()
        );
        Assert.Empty(handler.Recordings);
    }

    [Fact]
    public async Task Post_UnderTheDryRunVariable_IsPreviewedAsWithTheOption()
    {
        // How an extension command's calls inherit --dry-run from the line that launched it.
        var handler = Wire.Blank();
        Environment.SetEnvironmentVariable(GlobalOptions.DryRunVariable, "1");
        try
        {
            var (exit, stdout, _) = await RunAsync(
                handler,
                $"api post {Language} --json-body {_bodyPath}"
            );

            Assert.Equal((0, "dry-run"), (exit, Json(stdout).GetProperty("status").GetString()));
            Assert.Empty(handler.Recordings);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GlobalOptions.DryRunVariable, null);
        }
    }

    [Fact]
    public async Task Get_NotInTheAllowList_IsRefusedAndSendsNothing()
    {
        var handler = Wire.Returning("{}");

        var (exit, _, stderr) = await RunAsync(handler, $"api get {Status}", "content,media");

        Assert.Equal((2, "not_allowed"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }

    [Fact]
    public async Task Get_AllowListNamingApiGet_Runs()
    {
        var handler = Wire.Returning("{}");

        var (exit, _, _) = await RunAsync(handler, $"api get {Status}", "content,api.get");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Post_AllowListNamingOnlyApiGet_IsRefused()
    {
        // One verb per method is what lets an allow-list permit raw reads but not raw writes.
        var handler = Wire.Blank();

        var (exit, _, stderr) = await RunAsync(
            handler,
            $"api post {Language} --json-body {_bodyPath}",
            "api.get"
        );

        Assert.Equal((2, "not_allowed"), (exit, Category(stderr)));
    }

    [Fact]
    public async Task Delete_NonInteractiveWithoutYes_IsRefusedAndSendsNothing()
    {
        var handler = Wire.Blank();

        var (exit, _, stderr) = await RunAsync(handler, $"api delete {Language}/da-DK");

        Assert.Equal((2, "confirmation_required"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }

    [Fact]
    public async Task Delete_WithYes_SendsTheDelete()
    {
        var handler = Wire.Blank();

        var (exit, _, _) = await RunAsync(handler, $"api delete {Language}/da-DK --yes");

        Assert.Equal(0, exit);
        handler.AssertRequested(HttpMethod.Delete, "/language/da-DK");
    }

    [Theory]
    [InlineData("api get /somewhere/else")]
    [InlineData("api get https://other.example/umbraco/management/api/v1/server/status")]
    [InlineData("api get /umbraco/../somewhere/else")]
    public async Task PathOutsideUmbraco_IsAnInvalidArgumentErrorAndSendsNothing(string commandLine)
    {
        var handler = Wire.Blank();

        var (exit, _, stderr) = await RunAsync(handler, commandLine);

        Assert.Equal((1, "invalid_argument"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }

    [Theory]
    [InlineData("api fetch /umbraco/management/api/v1/server/status")]
    [InlineData("api GET /umbraco/management/api/v1/server/status")] // verbs are lower case
    [InlineData("api head /umbraco/management/api/v1/server/status")]
    public async Task MethodWithNoVerb_IsAnInvalidArgumentErrorAndSendsNothing(string commandLine)
    {
        var handler = Wire.Blank();

        var (exit, _, stderr) = await RunAsync(handler, commandLine);

        Assert.Equal((1, "invalid_argument"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }

    [Fact]
    public async Task Post_BodyThatIsNotJson_IsAnInvalidArgumentErrorAndSendsNothing()
    {
        var handler = Wire.Blank();
        File.WriteAllText(_bodyPath, "{ not json");

        var (exit, _, stderr) = await RunAsync(
            handler,
            $"api post {Language} --json-body {_bodyPath}"
        );

        Assert.Equal((1, "invalid_argument"), (exit, Category(stderr)));
        Assert.Empty(handler.Recordings);
    }
}
