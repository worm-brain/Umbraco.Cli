using System.ComponentModel;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Extensions;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What running an extension command does (ADR 0010): the allow-list treats its noun as a group,
/// a <c>--host</c> the credentials do not belong to is refused before it can become
/// <c>UMBRACO_HOST</c>, and otherwise the executable runs with the line's arguments and context
/// and its exit code is the CLI's. The process start is replaced by a recorder, except in the one
/// test that really spawns a child.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class ExtensionLauncherTests : IDisposable
{
    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-ext-launch-{Guid.NewGuid():N}.json"
    );

    /// <summary>What the recorder was asked to run, or null when nothing was run.</summary>
    private (
        string Executable,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment
    )? _ran;

    public ExtensionLauncherTests()
    {
        // The allow-list reads process env; a developer machine must not perturb these tests.
        Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", null);
    }

    public void Dispose() => File.Delete(_configPath);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>A launcher over a default config holding <paramref name="configJson"/>, whose runs return <paramref name="exitCode"/>.</summary>
    private ExtensionLauncher Launcher(string? configJson = null, int exitCode = 0)
    {
        if (configJson is not null)
            File.WriteAllText(_configPath, configJson);
        var http = new StubHttpClientFactory();
        var contexts = new CommandContextFactory(
            new ConfigStore(_configPath),
            new UmbracoAuthService(http),
            http,
            new GlobalOptions(),
            new UmbracoManagementClientFactory(),
            new MutationInterceptState()
        );
        return new ExtensionLauncher(
            contexts,
            (exe, args, env, _) =>
            {
                _ran = (exe, args, env);
                return Task.FromResult(exitCode);
            }
        );
    }

    private static ExtensionInvocation Foo(
        ExtensionContext? context = null,
        string? problem = null
    ) =>
        new(
            "foo",
            ["export", "--x", "1"],
            context ?? new ExtensionContext(Output: "json"),
            problem
        );

    private static async Task<(int Exit, string Stderr)> RunAsync(
        ExtensionLauncher launcher,
        ExtensionInvocation invocation
    )
    {
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var exit = await launcher.RunAsync(invocation, "/tools/umbraco-foo");
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private static string? CategoryIn(string stderr) =>
        JsonDocument.Parse(stderr).RootElement.GetProperty("category").GetString();

    [Fact]
    public async Task RunAsync_Allowed_RunsTheExecutableWithTheArgumentsAndReturnsItsExitCode()
    {
        var launcher = Launcher(exitCode: 7);

        var (exit, _) = await RunAsync(launcher, Foo());

        Assert.Equal(7, exit);
        Assert.Equal("/tools/umbraco-foo", _ran?.Executable);
        Assert.Equal(["export", "--x", "1"], _ran?.Arguments);
    }

    [Fact]
    public async Task RunAsync_ContextOptions_ReachTheExtensionsEnvironment()
    {
        var launcher = Launcher();

        await RunAsync(launcher, Foo(new ExtensionContext(Profile: "staging", DryRun: true)));

        Assert.Equal("staging", _ran?.Environment["UMBRACO_PROFILE"]);
        Assert.Equal("1", _ran?.Environment["UMBRACO_DRY_RUN"]);
    }

    [Fact]
    public async Task RunAsync_NounNotInTheAllowList_IsRefusedWithoutRunning()
    {
        var launcher = Launcher("""{"allowedCommands":"content,api.get"}""");

        var (exit, stderr) = await RunAsync(launcher, Foo());

        Assert.Equal((2, "not_allowed"), (exit, CategoryIn(stderr)));
        Assert.Null(_ran);
    }

    [Fact]
    public async Task RunAsync_NounInTheAllowList_Runs()
    {
        var launcher = Launcher("""{"allowedCommands":"content,foo"}""");

        var (exit, _) = await RunAsync(launcher, Foo());

        Assert.Equal(0, exit);
        Assert.NotNull(_ran);
    }

    private const string SiteProfile = """
        {"defaultProfile":"default","profiles":{"default":
          {"host":"https://site.example","clientId":"umbraco-back-office-cli","clientSecret":"s3cret"}}}
        """;

    [Fact]
    public async Task RunAsync_HostTheCredentialsDoNotBelongTo_IsRefusedWithoutRunning()
    {
        var launcher = Launcher(SiteProfile);

        var (exit, stderr) = await RunAsync(
            launcher,
            Foo(new ExtensionContext(Host: "https://other.example"))
        );

        Assert.Equal((2, "refused"), (exit, CategoryIn(stderr)));
        Assert.Null(_ran);
    }

    [Fact]
    public async Task RunAsync_OtherHostWithItsOwnToken_Runs()
    {
        var launcher = Launcher(SiteProfile);

        var (exit, _) = await RunAsync(
            launcher,
            Foo(new ExtensionContext(Host: "https://other.example", Token: "t0ken"))
        );

        Assert.Equal(0, exit);
        Assert.Equal("t0ken", _ran?.Environment["UMBRACO_TOKEN"]);
    }

    [Fact]
    public async Task RunAsync_MalformedContextOption_IsAnInvalidArgumentError()
    {
        var launcher = Launcher();

        var (exit, stderr) = await RunAsync(launcher, Foo(problem: "--profile needs a value."));

        Assert.Equal((1, "invalid_argument"), (exit, CategoryIn(stderr)));
        Assert.Null(_ran);
    }

    [Fact]
    public async Task RunAsync_ExecutableThatWillNotStart_IsAnError()
    {
        var http = new StubHttpClientFactory();
        var launcher = new ExtensionLauncher(
            new CommandContextFactory(
                new ConfigStore(_configPath),
                new UmbracoAuthService(http),
                http,
                new GlobalOptions(),
                new UmbracoManagementClientFactory(),
                new MutationInterceptState()
            ),
            (_, _, _, _) => throw new Win32Exception("Access is denied.")
        );

        var (exit, stderr) = await RunAsync(launcher, Foo());

        Assert.Equal((1, "internal"), (exit, CategoryIn(stderr)));
    }

    /// <summary>The <c>dotnet</c> host running these tests, which every machine that builds them has.</summary>
    private static string Dotnet() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { } host && File.Exists(host)
            ? host
            : (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Select(dir =>
                    Path.Combine(dir, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
                )
                .First(File.Exists);

    [Fact]
    public async Task ExtensionProcess_RealChildThatSucceeds_ReturnsZero()
    {
        var exit = await ExtensionProcess.RunAsync(
            Dotnet(),
            ["--list-runtimes"],
            new Dictionary<string, string>()
        );

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task ExtensionProcess_RealChildThatFails_ReturnsItsExitCode()
    {
        var exit = await ExtensionProcess.RunAsync(
            Dotnet(),
            ["no-such-command-for-this-test"],
            new Dictionary<string, string>()
        );

        Assert.NotEqual(0, exit);
    }
}
