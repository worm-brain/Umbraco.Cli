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

    /// <summary>Test double for the destructive-op confirmation (#70): canned interactivity + answer.</summary>
    private sealed class FakeConfirmationPrompt : Umbraco.Cli.Infrastructure.IConfirmationPrompt
    {
        public bool IsInteractive { get; init; }
        public bool Answer { get; init; }
        public bool WasPrompted { get; private set; }

        public bool Confirm(string message)
        {
            WasPrompted = true;
            return Answer;
        }
    }

    private static (CommandExecutor executor, ParseResult parse) Build(
        IUmbracoManagementClient client,
        string args = "--host https://example.com --token tok --output json",
        Umbraco.Cli.Infrastructure.IConfirmationPrompt? confirmation = null
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
        var executor = new CommandExecutor(
            factory,
            confirmation ?? new FakeConfirmationPrompt { IsInteractive = false }
        );

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
    public async Task Destructive_NonInteractiveWithoutYes_AbortsAndDoesNotCall()
    {
        // #70: a destructive command run non-interactively (no TTY) without --yes must abort
        // (exit 2) and never invoke the client - a delete can't happen silently in a script.
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            confirmation: new FakeConfirmationPrompt { IsInteractive = false }
        );

        var (_, stderr, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                "content.delete",
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
                },
                "Deleted.",
                CancellationToken.None,
                confirmationPrompt: "Delete X?"
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called);
        Assert.Contains("--yes", stderr);
    }

    [Fact]
    public async Task Destructive_WithYes_RunsWithoutPrompting()
    {
        // --yes bypasses the gate entirely, even non-interactively.
        var called = false;
        var prompt = new FakeConfirmationPrompt { IsInteractive = true, Answer = false };
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            args: "--host https://example.com --token tok --output json --yes",
            confirmation: prompt
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                "content.delete",
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
                },
                "Deleted.",
                CancellationToken.None,
                confirmationPrompt: "Delete X?"
            )
        );

        Assert.Equal(0, exit);
        Assert.True(called);
        Assert.False(prompt.WasPrompted); // --yes skips the prompt
    }

    [Fact]
    public async Task Destructive_InteractiveDecline_AbortsAndDoesNotCall()
    {
        // Interactive, user answers "no" → abort, no client call.
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            confirmation: new FakeConfirmationPrompt { IsInteractive = true, Answer = false }
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                "content.delete",
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
                },
                "Deleted.",
                CancellationToken.None,
                confirmationPrompt: "Delete X?"
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called);
    }

    [Fact]
    public async Task Destructive_InteractiveConfirm_Runs()
    {
        // Interactive, user answers "yes" → the delete proceeds.
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            confirmation: new FakeConfirmationPrompt { IsInteractive = true, Answer = true }
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                "content.delete",
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
                },
                "Deleted.",
                CancellationToken.None,
                confirmationPrompt: "Delete X?"
            )
        );

        Assert.Equal(0, exit);
        Assert.True(called);
    }

    [Fact]
    public async Task Forbidden_TranslatesToPermissionMessage()
    {
        // #70: a raw 403 must become an actionable permission message.
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(403, "Forbidden"),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                "content.get",
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(stderr);
        var message = doc.RootElement.GetProperty("message").GetString();
        Assert.Contains("not permitted", message);
        Assert.Contains("permissions", message);
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
