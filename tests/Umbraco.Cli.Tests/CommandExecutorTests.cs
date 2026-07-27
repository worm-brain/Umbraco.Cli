using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

// Console.SetOut/SetError are process-global, so we serialise with the other capture tests.
[Collection("ConsoleCapture")]
public class CommandExecutorTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory : IUmbracoManagementClientFactory
    {
        private readonly IUmbracoManagementClient _client;

        public FakeClientFactory(IUmbracoManagementClient client) => _client = client;

        public IUmbracoManagementClient Create(HttpClient http) => _client;
    }

    private static (CommandExecutor executor, ParseResult parse) Build(
        IUmbracoManagementClient client,
        string args = "--host https://example.com --token tok --output json"
    )
    {
        var stub = new StubHttpClientFactory();
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-exec-test-{Guid.NewGuid()}.json")
        );
        var authService = new UmbracoAuthService(stub);
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            authService,
            stub,
            global,
            new FakeClientFactory(client),
            new Umbraco.Cli.Infrastructure.Http.MutationInterceptState()
        );
        var executor = new CommandExecutor(factory);

        var root = new RootCommand();
        global.AddTo(root);
        return (executor, root.Parse(args));
    }

    private static async Task<(string stdout, string stderr, int exit)> Capture(
        Func<Task<int>> action
    )
    {
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        var origOut = Console.Out;
        var origErr = Console.Error;
        Console.SetOut(outSw);
        Console.SetError(errSw);
        try
        {
            var exit = await action();
            return (outSw.ToString(), errSw.ToString(), exit);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    // --host + --token skip the config/auth path, so these run with no real HTTP or credentials.

    [Fact]
    public async Task RunObject_ApiSuccess_RendersSuccessAndReturnsZero()
    {
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse { Id = Guid.NewGuid(), Name = "About" }
            ),
        };
        var (executor, parse) = Build(client);

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                "content.get",
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("About", doc.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunObject_ApiFailure_WritesErrorAndReturnsOne()
    {
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(404, "Not found"),
        };
        var (executor, parse) = Build(client);

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                "content.get",
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(404, doc.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Not found", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RunObject_PassesParsedArgumentToClientCall()
    {
        var id = Guid.NewGuid();
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse()
            ),
        };
        var (executor, parse) = Build(client);

        await Capture(() =>
            executor.RunObjectAsync(
                parse,
                "content.get",
                (c, ct) => c.GetContentByIdAsync(id, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(id, client.LastRequestedId);
    }

    [Fact]
    public async Task RunObject_CallThrows_BackstopWritesErrorAndReturnsOne()
    {
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync<ContentItemResponse>(
                parse,
                "content.create",
                (c, ct) => throw new InvalidOperationException("Invalid JSON body."),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains("Invalid JSON body", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RunObject_CallThrowsDryRun_WritesPreviewToStdoutAndReturnsZero()
    {
        // #62: a DryRunException from the interceptor must be rendered as a dry-run preview on
        // stdout (not treated as an error by the backstop) and exit 0 - nothing was mutated.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync<ContentItemResponse>(
                parse,
                "content.create",
                (c, ct) =>
                    throw new Umbraco.Cli.Infrastructure.Http.DryRunException(
                        "POST",
                        "https://example.com/umbraco/management/api/v1/document",
                        """{"name":"x"}"""
                    ),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("dry-run", doc.RootElement.GetProperty("status").GetString());
        var request = doc.RootElement.GetProperty("request");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Contains("document", request.GetProperty("url").GetString());
        // A valid-JSON body is embedded as nested JSON, not a string.
        Assert.Equal("x", request.GetProperty("body").GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunObject_NoHostOrCredentials_AbortsWithTwo()
    {
        // No --host and an empty config → CreateAsync aborts before any client call.
        var (executor, parse) = Build(new FakeUmbracoManagementClient(), args: "--output json");

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                "content.get",
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
    }
}
