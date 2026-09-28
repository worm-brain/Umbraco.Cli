using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

// Console.SetOut/SetError are process-global, so we serialise with the other capture tests.
[Collection("ConsoleCapture")]
public class CommandExecutorTests
{
    public CommandExecutorTests()
    {
        // The allow-list / read-only guardrails read process env; clear them so a developer
        // machine that happens to have them set does not perturb these tests (#69).
        Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", null);
        Environment.SetEnvironmentVariable("UMBRACO_READONLY", null);
    }

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
        Umbraco.Cli.Infrastructure.IConfirmationPrompt? confirmation = null,
        string? allowedCommands = null,
        Umbraco.Cli.Infrastructure.Http.MutationInterceptState? mutationState = null,
        string command = "content.get"
    )
    {
        var stub = new StubHttpClientFactory();
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"umbraco-exec-test-{Guid.NewGuid()}.json"
        );
        // Write a config carrying only the allow-list (#69) when a test supplies one; auth is
        // provided via --host/--token overrides, so no credentials are needed in the file.
        if (allowedCommands is not null)
            File.WriteAllText(configPath, $$"""{"allowedCommands":"{{allowedCommands}}"}""");
        var configStore = new ConfigStore(configPath);
        var authService = new UmbracoAuthService(stub);
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            authService,
            stub,
            global,
            new FakeClientFactory(client),
            mutationState ?? new Umbraco.Cli.Infrastructure.Http.MutationInterceptState()
        );
        var executor = new CommandExecutor(
            factory,
            confirmation ?? new FakeConfirmationPrompt { IsInteractive = false }
        );

        // A real command path, because the executor derives meta.command and the allow-list
        // name from the parse tree.
        var root = new RootCommand();
        global.AddTo(root);
        Command parent = root;
        foreach (var segment in command.Split('.'))
        {
            var child = new Command(segment);
            parent.Add(child);
            parent = child;
        }
        return (executor, root.Parse($"{command.Replace('.', ' ')} {args}"));
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
    public async Task RunObject_ApiFailureWithABody_WritesItAsDetails()
    {
        // #286: Umbraco's ProblemDetails reach the envelope as they were sent.
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(
                400,
                "Invalid document (ContentInvalid).",
                FailureCategory.RequestRejected,
                System.Text.Json.Nodes.JsonNode.Parse(
                    """{"title":"Invalid document","operationStatus":"ContentInvalid"}"""
                )
            ),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, _) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal(
            "ContentInvalid",
            doc.RootElement.GetProperty("details").GetProperty("operationStatus").GetString()
        );
    }

    [Fact]
    public async Task RunObject_ApiFailureWithoutABody_HasNoDetails()
    {
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(404, "Not found"),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, _) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        using var doc = JsonDocument.Parse(stderr);
        Assert.False(doc.RootElement.TryGetProperty("details", out _));
    }

    [Fact]
    public void FailureFrom_CarriesTheDetailsAcross()
    {
        // A command that reads before it writes surfaces the read's failure, body included.
        var read = UmbracoResponse<string>.Failure(
            500,
            "Boom.",
            FailureCategory.ServerError,
            System.Text.Json.Nodes.JsonNode.Parse("""{"title":"Boom"}""")
        );

        var rewrapped = UmbracoResponse<int>.FailureFrom(read);

        Assert.Equal("""{"title":"Boom"}""", rewrapped.Details?.ToJsonString());
    }

    [Fact]
    public void FailureFrom_CarriesTheUnknownValuesAcross()
    {
        // #278: a re-wrapped known-value refusal keeps what the suggestion is built from.
        var values = new UnknownValues(["ContentPublished"], ["Umbraco.ContentPublish"]);
        var read = UmbracoResponse<string>.Failure(0, "Unknown.") with { UnknownValues = values };

        var rewrapped = UmbracoResponse<int>.FailureFrom(read);

        Assert.Same(values, rewrapped.UnknownValues);
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
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(404, doc.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("Not found", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RunObject_ServerErrorFailure_TagsCategoryAndServerVersion()
    {
        // #152: a server-side failure is tagged with its category and the connected server
        // version, so a caller can attribute it. The fake reports version 14.0.0.
        var client = new FakeUmbracoManagementClient
        {
            ServerVersion = "17.3.5",
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(
                500,
                "boom",
                FailureCategory.ServerError
            ),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("server_error", doc.RootElement.GetProperty("category").GetString());
        Assert.Equal("17.3.5", doc.RootElement.GetProperty("serverVersion").GetString());
    }

    /// <summary>Runs a command whose call fails as an unexpected response (#154).</summary>
    /// <param name="serverVersion">The version the fake server reports.</param>
    /// <returns>The error envelope's <c>category</c> and <c>message</c>.</returns>
    private async Task<(string? Category, string? Message)> RunUnexpectedResponse(
        string? serverVersion
    )
    {
        var client = new FakeUmbracoManagementClient
        {
            ServerVersion = serverVersion,
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(
                0,
                "Unreadable.",
                FailureCategory.UnexpectedResponse
            ),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, _) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        using var doc = JsonDocument.Parse(stderr);
        return (
            doc.RootElement.GetProperty("category").GetString(),
            doc.RootElement.GetProperty("message").GetString()
        );
    }

    [Fact]
    public void CategoryOf_JsonExceptionFromTheCallersInput_IsInvalidArgument()
    {
        // A response-side JsonException never gets here (the client guard labels it, #154), so
        // one the backstop sees is a snapshot or body the caller supplied.
        Assert.Equal(
            FailureCategory.InvalidArgument,
            CommandExecutor.CategoryOf(new JsonException("bad snapshot"))
        );
    }

    [Fact]
    public void CategoryOf_UnrecognisedException_IsInternal()
    {
        Assert.Equal(
            FailureCategory.Internal,
            CommandExecutor.CategoryOf(new InvalidOperationException("bug"))
        );
    }

    [Fact]
    public async Task RunObject_UnexpectedResponse_KeepsTheCategory()
    {
        var (category, _) = await RunUnexpectedResponse("17.3.5");

        Assert.Equal("unexpected_response", category);
    }

    [Fact]
    public async Task RunObject_UnexpectedResponse_PointsAtAuthDoctor()
    {
        var (_, message) = await RunUnexpectedResponse("17.3.5");

        Assert.Equal(
            "Unreadable. Run 'umbraco auth doctor' to check the instance's Umbraco version.",
            message
        );
    }

    [Fact]
    public async Task RunObject_UnexpectedResponseFromAnUnsupportedVersion_NamesTheRange()
    {
        // #153: when the version is known to be out of range, say so instead of "go and check".
        var (_, message) = await RunUnexpectedResponse("99.0.0");

        Assert.Equal($"Unreadable. {VersionSupport.OutOfRangeMessage("99.0.0")}", message);
    }

    [Fact]
    public async Task RunObject_UnreachableFailure_TagsCategoryButSkipsServerVersion()
    {
        // #152: for an unreachable/timeout failure the server cannot be queried, so the version
        // lookup is skipped and the field is omitted - but the category is still reported.
        var client = new FakeUmbracoManagementClient
        {
            ServerVersion = "17.3.5", // would be returned if (wrongly) queried
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Failure(
                0,
                "Could not reach the Umbraco instance: no such host",
                FailureCategory.Unreachable
            ),
        };
        var (executor, parse) = Build(client);

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("unreachable", doc.RootElement.GetProperty("category").GetString());
        Assert.False(doc.RootElement.TryGetProperty("serverVersion", out _));
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
                (c, ct) => throw new InvalidInputException("Invalid JSON body."),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains("Invalid JSON body", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("invalid_argument", doc.RootElement.GetProperty("category").GetString());
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
        // #165: under `data`, like every other success envelope.
        var request = doc.RootElement.GetProperty("data");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Contains("document", request.GetProperty("url").GetString());
        // A valid-JSON body is embedded as nested JSON, not a string.
        Assert.Equal("x", request.GetProperty("body").GetProperty("name").GetString());
        Assert.Equal(
            "content.get",
            JsonDocument
                .Parse(stdout)
                .RootElement.GetProperty("meta")
                .GetProperty("command")
                .GetString()
        );
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
        parse.CommandResult.Command.Destructive(_ => "Delete X?");

        var (_, stderr, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<ItemRef>.Success(ItemRef.Of("1")));
                },
                "Deleted.",
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called);
        Assert.Contains("--yes", stderr);
        Assert.Equal("confirmation_required", CategoryIn(stderr));
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
        parse.CommandResult.Command.Destructive(_ => "Delete X?");

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<ItemRef>.Success(ItemRef.Of("1")));
                },
                "Deleted.",
                CancellationToken.None
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
        parse.CommandResult.Command.Destructive(_ => "Delete X?");

        var (_, stderr, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<ItemRef>.Success(ItemRef.Of("1")));
                },
                "Deleted.",
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called);
        Assert.Equal("cancelled", CategoryIn(stderr));
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
        parse.CommandResult.Command.Destructive(_ => "Delete X?");

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<ItemRef>.Success(ItemRef.Of("1")));
                },
                "Deleted.",
                CancellationToken.None
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
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(stderr);
        var message = doc.RootElement.GetProperty("message").GetString();
        Assert.Contains("not permitted", message);
        Assert.Contains("permissions", message);
        // The original API detail is preserved in parentheses.
        Assert.Contains("(Forbidden)", message);
        // The raw status code is still carried on the envelope.
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(403, doc.RootElement.GetProperty("httpStatus").GetInt32());
    }

    [Fact]
    public async Task Destructive_WithDryRun_SkipsGateWithoutYesOrPrompt()
    {
        // #70 x #62: --dry-run never sends the mutation, so the confirmation gate must be
        // skipped even non-interactively and without --yes (otherwise previewing a delete
        // would force --yes, teaching agents the always-pass-yes habit). Here the fake client
        // has no HTTP interceptor, so the call simply proceeds - proving the gate did not abort.
        var called = false;
        var prompt = new FakeConfirmationPrompt { IsInteractive = false };
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            args: "--host https://example.com --token tok --output json --dry-run",
            confirmation: prompt
        );
        parse.CommandResult.Command.Destructive(_ => "Delete X?");

        var (_, _, exit) = await Capture(() =>
            executor.RunMessageAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(UmbracoResponse<ItemRef>.Success(ItemRef.Of("1")));
                },
                "Deleted.",
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.True(called); // gate skipped under --dry-run
        Assert.False(prompt.WasPrompted);
    }

    [Fact]
    public async Task RunObject_CallThrowsReadOnly_WritesErrorAndReturnsTwo()
    {
        // #69: a ReadOnlyModeException from the interceptor becomes a clear read-only error
        // (exit 2), not a raw backstop error.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync<ContentItemResponse>(
                parse,
                (c, ct) =>
                    throw new Umbraco.Cli.Infrastructure.Http.ReadOnlyModeException(
                        "POST",
                        "https://example.com/umbraco/management/api/v1/document"
                    ),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains("Read-only", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("readonly", CategoryIn(stderr));
    }

    [Fact]
    public async Task AllowList_DisallowedCommand_AbortsWithTwo()
    {
        // #69: a command outside the allow-list is refused before running (exit 2).
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            allowedCommands: "content,media",
            command: "webhook.list"
        );

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return c.GetWebhooksAsync(0, 20, ct);
                },
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called); // aborted before the client call
        Assert.Contains(
            "allow-list",
            JsonDocument.Parse(stderr).RootElement.GetProperty("message").GetString()
        );
        Assert.Equal("not_allowed", CategoryIn(stderr));
    }

    [Fact]
    public async Task AllowList_AllowedGroup_Runs()
    {
        // A command whose group is in the allow-list runs normally.
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse()
            ),
        };
        var (executor, parse) = Build(client, allowedCommands: "content,media");

        var (_, _, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task AllowList_AuthAlwaysAllowed()
    {
        // The auth group is exempt from the allow-list, so an auth command runs even when the
        // allow-list would otherwise exclude it.
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            allowedCommands: "content",
            command: "auth.whoami"
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return Task.FromResult(
                        UmbracoResponse<CurrentUserResponse>.Success(new CurrentUserResponse())
                    );
                },
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.True(called); // not aborted by the allow-list
    }

    [Fact]
    public async Task AllowList_ConfigOverrideWithoutAllowList_DoesNotBypassDefaultAllowList()
    {
        // #83 M2: the allow-list is a supervisor guardrail, so pointing --config at a file without
        // an allow-list must not escape the default store's restriction. The most-restrictive of
        // the two applies, so a command outside the default allow-list is still refused.
        var bypassConfig = Path.Combine(
            Path.GetTempPath(),
            $"umbraco-bypass-{Guid.NewGuid()}.json"
        );
        File.WriteAllText(bypassConfig, "{}"); // a valid config that carries no allow-list
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            args: $"--host https://example.com --token tok --output json --config \"{bypassConfig}\"",
            allowedCommands: "content",
            command: "webhook.list"
        );

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return c.GetWebhooksAsync(0, 20, ct);
                },
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called); // the --config file could not loosen the default allow-list
        Assert.Contains(
            "allow-list",
            JsonDocument.Parse(stderr).RootElement.GetProperty("message").GetString()
        );
    }

    [Fact]
    public async Task AllowList_ConfigProfileNotInDefaultStore_DoesNotBypassDefaultAllowList()
    {
        // #83 M2 (profile axis): a --profile that exists only in a --config file must not escape the
        // default store's allow-list. The baseline falls back to the default store's default profile
        // rather than resolving to an empty, unrestricted config for the unknown profile.
        var attackerConfig = Path.Combine(
            Path.GetTempPath(),
            $"umbraco-ghost-{Guid.NewGuid()}.json"
        );
        File.WriteAllText(
            attackerConfig,
            """{"profiles":{"ghost":{"host":"https://evil.example","allowedCommands":"webhooks"}},"defaultProfile":"ghost"}"""
        );
        var called = false;
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            args: $"--host https://example.com --token tok --output json --config \"{attackerConfig}\" --profile ghost",
            allowedCommands: "content",
            command: "webhook.list"
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return c.GetWebhooksAsync(0, 20, ct);
                },
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called); // a --config-only profile could not loosen the default allow-list
    }

    [Fact]
    public async Task AllowList_WhitespaceValue_IsExplicitLockdown_DeniesNonAuthCommand()
    {
        // #83 L3: a present-but-blank allow-list (e.g. UMBRACO_ALLOWED_COMMANDS=" ") is an explicit
        // lockdown, not "allow everything" — non-auth commands are refused.
        var called = false;
        var (executor, parse) = Build(new FakeUmbracoManagementClient(), allowedCommands: " ");

        var (_, _, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                {
                    called = true;
                    return c.GetContentByIdAsync(Guid.NewGuid(), ct);
                },
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.False(called);
    }

    [Fact]
    public async Task AllowList_WhitespaceLockdown_StillAllowsAuth()
    {
        // Even under an explicit lockdown the auth group stays exempt, so the session can still
        // authenticate and be inspected (#83 L3).
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            allowedCommands: " ",
            command: "auth.whoami"
        );

        var (_, _, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                    Task.FromResult(
                        UmbracoResponse<CurrentUserResponse>.Success(new CurrentUserResponse())
                    ),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task ReadOnlyFlag_SetsBlockPolicy()
    {
        // Wiring: --readonly flips the shared interceptor policy to Block for the invocation.
        var state = new Umbraco.Cli.Infrastructure.Http.MutationInterceptState();
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse()
            ),
        };
        var (executor, parse) = Build(
            client,
            args: "--host https://example.com --token tok --output json --readonly",
            mutationState: state
        );

        await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(Umbraco.Cli.Infrastructure.Http.MutationInterceptPolicy.Block, state.Policy);
    }

    [Fact]
    public async Task DryRun_BeatsReadOnly_InPolicy()
    {
        // Precedence: --dry-run (Preview) wins over --readonly (Block).
        var state = new Umbraco.Cli.Infrastructure.Http.MutationInterceptState();
        var client = new FakeUmbracoManagementClient
        {
            ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse()
            ),
        };
        var (executor, parse) = Build(
            client,
            args: "--host https://example.com --token tok --output json --dry-run --readonly",
            mutationState: state
        );

        await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(Umbraco.Cli.Infrastructure.Http.MutationInterceptPolicy.Preview, state.Policy);
    }

    [Fact]
    public async Task ReadOnlyEnvVar_SetsBlockPolicy()
    {
        // UMBRACO_READONLY (truthy) is honoured like the flag.
        Environment.SetEnvironmentVariable("UMBRACO_READONLY", "1");
        try
        {
            var state = new Umbraco.Cli.Infrastructure.Http.MutationInterceptState();
            var client = new FakeUmbracoManagementClient
            {
                ContentByIdResponse = UmbracoResponse<ContentItemResponse>.Success(
                    new ContentItemResponse()
                ),
            };
            var (executor, parse) = Build(client, mutationState: state);

            await Capture(() =>
                executor.RunObjectAsync(
                    parse,
                    (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                    CancellationToken.None
                )
            );

            Assert.Equal(
                Umbraco.Cli.Infrastructure.Http.MutationInterceptPolicy.Block,
                state.Policy
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMBRACO_READONLY", null);
        }
    }

    // ── Bulk operations (#85) ───────────────────────────────────────────────

    [Fact]
    public async Task RunBulk_AllSucceed_ReturnsZeroWithPerItemResults()
    {
        // #85: a non-destructive bulk publish over two ids runs each and returns a results array.
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var client = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(client);

        var (stdout, _, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [id1.ToString(), id2.ToString()],
                (c, id, ct) => c.PublishContentAsync(id, null, ct: ct),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.Equal([id1, id2], client.CalledIds);
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
        Assert.All(
            data.EnumerateArray(),
            item => Assert.Equal("success", item.GetProperty("status").GetString())
        );
    }

    [Fact]
    public async Task RunBulk_MixedOutcomes_ReturnsOneAndReportsEachItem()
    {
        // #85: a per-item failure and a malformed id are captured (not fatal); the batch still
        // runs every valid id and the exit code is 1 because something failed.
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var client = new FakeUmbracoManagementClient
        {
            PublishContentHandler = id =>
                id == bad
                    ? UmbracoResponse<Empty>.Failure(404, "Not found")
                    : UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(client);

        var (stdout, _, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [ok.ToString(), bad.ToString(), "not-a-guid"],
                (c, id, ct) => c.PublishContentAsync(id, null, ct: ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.Equal(3, data.GetArrayLength());
        Assert.Equal("success", data[0].GetProperty("status").GetString());
        Assert.Equal("error", data[1].GetProperty("status").GetString());
        Assert.Equal("error", data[2].GetProperty("status").GetString()); // malformed id
        Assert.Equal("not-a-guid", data[2].GetProperty("id").GetString());
    }

    /// <summary>Runs a bulk publish over <paramref name="ids"/>, failing the ids in <paramref name="failing"/>.</summary>
    private static async Task<JsonElement> BulkEnvelope(
        IReadOnlyList<string> ids,
        IReadOnlySet<Guid> failing,
        string args = "--host https://example.com --token tok --output json"
    )
    {
        var client = new FakeUmbracoManagementClient
        {
            PublishContentHandler = id =>
                failing.Contains(id)
                    ? UmbracoResponse<Empty>.Failure(404, "Not found")
                    : UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(client, args);
        var (stdout, _, _) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => ids,
                (c, id, ct) => c.PublishContentAsync(id, null, ct: ct),
                CancellationToken.None
            )
        );
        return JsonDocument.Parse(stdout).RootElement.Clone();
    }

    [Fact]
    public async Task RunBulk_AllSucceed_StatusIsSuccess()
    {
        var envelope = await BulkEnvelope([Guid.NewGuid().ToString()], new HashSet<Guid>());

        Assert.Equal("success", envelope.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunBulk_SomeFailed_StatusIsPartialWithASummary()
    {
        // #236: the envelope said "success" while the exit code said 1.
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();

        var envelope = await BulkEnvelope(
            [ok.ToString(), bad.ToString()],
            new HashSet<Guid> { bad }
        );

        Assert.Equal("partial", envelope.GetProperty("status").GetString());
        var summary = envelope.GetProperty("meta").GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task RunBulk_AllFailed_StatusIsError()
    {
        var bad = Guid.NewGuid();

        var envelope = await BulkEnvelope(
            [bad.ToString(), "not-a-guid"],
            new HashSet<Guid> { bad }
        );

        Assert.Equal("error", envelope.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunBulk_DryRun_EachItemCarriesTheRequestItWouldSend()
    {
        // #236: a bulk dry run showed only {id, status}, so it could not be checked.
        var client = new FakeUmbracoManagementClient();
        var (executor, parse) = Build(
            client,
            "--host https://example.com --token tok --output json --dry-run"
        );
        var id = Guid.NewGuid();

        var (stdout, _, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [id.ToString()],
                (c, i, ct) =>
                    throw new Umbraco.Cli.Infrastructure.Http.DryRunException(
                        "PUT",
                        $"https://example.com/umbraco/management/api/v1/document/{i}/publish",
                        """{"publishSchedules":[{"culture":"en-US"}]}"""
                    ),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("dry-run", root.GetProperty("status").GetString());
        var request = root.GetProperty("data")[0].GetProperty("request");
        Assert.Equal("PUT", request.GetProperty("method").GetString());
        Assert.Equal(
            "en-US",
            request
                .GetProperty("body")
                .GetProperty("publishSchedules")[0]
                .GetProperty("culture")
                .GetString()
        );
    }

    [Fact]
    public async Task RunBulk_DestructiveNonInteractiveWithoutYes_AbortsAndDoesNotCall()
    {
        // #85 x #70: a bulk delete non-interactively without --yes aborts (exit 2) before any
        // call — a destructive batch can't run silently.
        var client = new FakeUmbracoManagementClient
        {
            DeleteContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(
            client,
            confirmation: new FakeConfirmationPrompt { IsInteractive = false }
        );
        parse.CommandResult.Command.Destructive(_ => "Delete all?");

        var (_, stderr, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [Guid.NewGuid().ToString()],
                (c, id, ct) => c.DeleteContentAsync(id, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Empty(client.CalledIds);
        Assert.Contains("--yes", stderr);
    }

    [Fact]
    public async Task RunBulk_DestructiveWithYes_RunsWholeBatchWithoutPerItemPrompt()
    {
        // --yes authorises the whole batch once; the prompt is never shown per item.
        var prompt = new FakeConfirmationPrompt { IsInteractive = true, Answer = false };
        var client = new FakeUmbracoManagementClient
        {
            DeleteContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(
            client,
            args: "--host https://example.com --token tok --output json --yes",
            confirmation: prompt
        );
        parse.CommandResult.Command.Destructive(_ => "Delete all?");

        var (_, _, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()],
                (c, id, ct) => c.DeleteContentAsync(id, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.Equal(2, client.CalledIds.Count);
        Assert.False(prompt.WasPrompted); // --yes skips the gate
    }

    [Fact]
    public async Task RunBulk_ReadOnly_ReturnsTwoWithoutRunning()
    {
        // #85 review fix: a bulk write under --readonly is blocked before running (exit 2), like
        // the single-op path — not run-then-report-all-failed (exit 1).
        var state = new Umbraco.Cli.Infrastructure.Http.MutationInterceptState();
        var client = new FakeUmbracoManagementClient
        {
            DeleteContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(
            client,
            args: "--host https://example.com --token tok --output json --readonly --yes",
            mutationState: state
        );
        parse.CommandResult.Command.Destructive(_ => "Delete all?");

        var (_, stderr, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [Guid.NewGuid().ToString()],
                (c, id, ct) => c.DeleteContentAsync(id, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Empty(client.CalledIds);
        Assert.Contains("Read-only", stderr);
    }

    [Fact]
    public async Task RunBulk_Unpublish_RunsEachId()
    {
        // #85: the unpublish bulk path runs each id (covers the third bulk verb).
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var client = new FakeUmbracoManagementClient
        {
            UnpublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var (executor, parse) = Build(client);

        var (_, _, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [id1.ToString(), id2.ToString()],
                (c, id, ct) => c.UnpublishContentAsync(id, null, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(0, exit);
        Assert.Equal([id1, id2], client.CalledIds);
    }

    [Fact]
    public async Task RunBulk_NoIds_IsInvalidArgument()
    {
        // An empty id set is a usage error, not a silent no-op.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());
        parse.CommandResult.Command.Destructive(_ => "Delete all?");

        var (_, stderr, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => [],
                (c, id, ct) => c.DeleteContentAsync(id, ct),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Equal(
            "invalid_argument",
            JsonDocument.Parse(stderr).RootElement.GetProperty("category").GetString()
        );
        Assert.Contains("No ids", stderr);
    }

    [Fact]
    public async Task RunBulk_MalformedInput_IsInvalidArgumentAndCallsNothing()
    {
        // #288: input BulkIds rejects (JSON items without an id, say) refuses the whole batch as
        // one invalid_argument error instead of running and failing item by item.
        var client = new FakeUmbracoManagementClient();
        var (executor, parse) = Build(client);

        var (_, stderr, exit) = await Capture(() =>
            executor.RunBulkAsync(
                parse,
                () => throw new InvalidInputException("The JSON input item [0] has no string id."),
                (c, id, ct) => c.PublishContentAsync(id, null, ct: ct),
                CancellationToken.None
            )
        );

        Assert.Equal(
            (1, "invalid_argument", 0),
            (exit, CategoryIn(stderr), client.CalledIds.Count)
        );
    }

    [Fact]
    public async Task RunObject_NoHostOrCredentials_AbortsWithTwo()
    {
        // No --host and an empty config → CreateAsync aborts before any client call.
        var (executor, parse) = Build(new FakeUmbracoManagementClient(), args: "--output json");

        var (stdout, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => c.GetContentByIdAsync(Guid.NewGuid(), ct),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("not_authenticated", CategoryIn(stderr));
    }

    /// <summary>The <c>category</c> of the error envelope on stderr.</summary>
    private static string? CategoryIn(string stderr) =>
        JsonDocument.Parse(stderr).RootElement.GetProperty("category").GetString();

    [Fact]
    public async Task RunObject_Success_MetaCommandIsTheParsedCommandPath()
    {
        // meta.command comes from the command tree, so a renamed command can never report its
        // old name.
        var (executor, parse) = Build(
            new FakeUmbracoManagementClient(),
            command: "content.domain.get"
        );

        var (stdout, _, _) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) => Task.FromResult(UmbracoResponse<string>.Success("ok")),
                CancellationToken.None
            )
        );

        Assert.Equal(
            "content.domain.get",
            JsonDocument
                .Parse(stdout)
                .RootElement.GetProperty("meta")
                .GetProperty("command")
                .GetString()
        );
    }

    [Fact]
    public async Task RunObject_CallThrowsUnexpectedly_IsReportedAsInternal()
    {
        // A failure that is not the caller's input is a CLI bug, and the category says so.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync<ContentItemResponse>(
                parse,
                (c, ct) => throw new NullReferenceException("boom"),
                CancellationToken.None
            )
        );

        Assert.Equal(1, exit);
        Assert.Equal("internal", CategoryIn(stderr));
    }

    [Fact]
    public async Task RunObject_CallRefuses_AbortsAsRefused()
    {
        // A pre-flight refusal (e.g. an in-use type without --force) is an abort, category refused.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (_, stderr, exit) = await Capture(() =>
            executor.RunObjectAsync<ContentItemResponse>(
                parse,
                (c, ct) => throw new SafetyRefusalException("In use. Pass --force."),
                CancellationToken.None
            )
        );

        Assert.Equal(2, exit);
        Assert.Equal("refused", CategoryIn(stderr));
    }

    [Fact]
    public async Task RunObject_ApiFailureWithNoCategory_IsClassifiedFromItsStatus()
    {
        // Every error carries a category, even when a client path forgot to set one.
        var (executor, parse) = Build(new FakeUmbracoManagementClient());

        var (_, stderr, _) = await Capture(() =>
            executor.RunObjectAsync(
                parse,
                (c, ct) =>
                    Task.FromResult(
                        new UmbracoResponse<string>
                        {
                            IsSuccess = false,
                            StatusCode = 400,
                            ErrorMessage = "Bad request.",
                        }
                    ),
                CancellationToken.None
            )
        );

        Assert.Equal("request_rejected", CategoryIn(stderr));
    }
}
