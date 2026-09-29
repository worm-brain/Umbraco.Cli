using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What one command line did: its exit code, what it printed, and every HTTP request it sent.
/// </summary>
/// <param name="CommandLine">The command line as run, without the global auth options.</param>
/// <param name="Exit">The process exit code.</param>
/// <param name="Stdout">Everything written to stdout.</param>
/// <param name="Stderr">Everything written to stderr.</param>
/// <param name="Requests">The requests the run sent, in order, token exchanges included.</param>
internal sealed record CliRun(
    string CommandLine,
    int Exit,
    string Stdout,
    string Stderr,
    IReadOnlyList<Recorded> Requests
);

/// <summary>
/// Runs the shipped command tree end to end over HTTP (#408): the real
/// <see cref="UmbracoManagementClient"/>, <see cref="UmbracoAuthService"/> and
/// <see cref="CommandContextFactory"/>, with every request - the token exchange and the Management
/// API calls alike - answered and recorded by one <see cref="RoutingHandler"/>. Only the transport
/// is a double, so a count reflects what the command would put on the wire.
/// <para>
/// Each instance is one CLI process: a new auth service over <see cref="TokenCache"/>, so two
/// instances sharing a cache model two runs of <c>umbraco</c> sharing the token file.
/// </para>
/// </summary>
internal sealed class HttpCli
{
    /// <summary>The host every run targets.</summary>
    public const string Host = "https://site.test";

    /// <summary>
    /// Global options for a run that brings its own bearer token, so no token exchange is made and
    /// the count is the command's own requests.
    /// </summary>
    private const string TokenAuth = $"--host {Host} --token t --output json";

    private readonly RoutingHandler _handler;
    private readonly ITokenCache? _tokenCache;
    private readonly CliConfig? _profile;

    /// <summary>Creates a CLI whose requests go to <paramref name="handler"/>.</summary>
    /// <param name="handler">Answers and records every request.</param>
    /// <param name="profile">
    /// The stored profile. Null means none, so every run must bring <c>--token</c>, which
    /// <see cref="RunAsync"/> does.
    /// </param>
    /// <param name="tokenCache">The token file, shared between instances; null keeps tokens in memory.</param>
    public HttpCli(
        RoutingHandler handler,
        CliConfig? profile = null,
        ITokenCache? tokenCache = null
    )
    {
        _handler = handler;
        _profile = profile;
        _tokenCache = tokenCache;
    }

    /// <summary>
    /// Runs <paramref name="commandLine"/> with a bearer token, so the requests recorded are the
    /// command's own. Stdout and stderr are captured, so callers must be in the
    /// <c>ConsoleCapture</c> collection.
    /// </summary>
    /// <param name="commandLine">The command and its options, e.g. <c>content list --all</c>.</param>
    /// <returns>What the run did.</returns>
    public Task<CliRun> RunAsync(string commandLine) =>
        RunRawAsync(commandLine, $"{TokenAuth} {commandLine}");

    /// <summary>
    /// Runs <paramref name="commandLine"/> on the stored profile's client credentials, so the run
    /// authenticates the way <c>umbraco</c> does in CI.
    /// </summary>
    /// <param name="commandLine">The command and its options.</param>
    /// <returns>What the run did, the token exchange included.</returns>
    public Task<CliRun> RunWithClientCredentialsAsync(string commandLine) =>
        RunRawAsync(commandLine, $"--output json {commandLine}");

    /// <summary>Builds a fresh tree, parses <paramref name="args"/> against it and invokes it.</summary>
    /// <param name="commandLine">The command line to report.</param>
    /// <param name="args">The full argument string.</param>
    /// <returns>What the run did; only the requests made by this run.</returns>
    private async Task<CliRun> RunRawAsync(string commandLine, string args)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"umbraco-rc-{Guid.NewGuid():N}.json");
        if (_profile is not null)
            new ConfigStore(configPath).Save(_profile);
        var before = _handler.Recordings.Count;

        var (origOut, origErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await BuildRoot(configPath).Parse(args).InvokeAsync();
            return new CliRun(
                commandLine,
                exit,
                stdout.ToString(),
                stderr.ToString(),
                [.. _handler.Recordings.Skip(before)]
            );
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            File.Delete(configPath);
        }
    }

    /// <summary>
    /// The tree <c>Program.cs</c> builds, with the HTTP pipeline replaced by the handler. The
    /// production delegating handlers (401 retry, verbose logging, dry-run interception) are left
    /// out: none of them adds a request to a run that succeeds first time.
    /// </summary>
    /// <param name="configPath">Where this run's config lives.</param>
    /// <returns>The root command.</returns>
    private RootCommand BuildRoot(string configPath)
    {
        var http = new HandlerHttpClientFactory(_handler);
        var clients = new UmbracoManagementClientFactory();
        var configStore = new ConfigStore(configPath);
        var auth = new UmbracoAuthService(http, cache: _tokenCache);
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            auth,
            http,
            global,
            clients,
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new ConsoleConfirmationPrompt());
        return CliRoot.Build(global, configStore, auth, executor, http, clients);
    }

    /// <summary>
    /// Hands out clients over the one handler whatever the name, so the token exchange (the
    /// default client) and the Management API calls (<c>umbraco</c>) are recorded together.
    /// </summary>
    /// <param name="handler">The shared handler; not disposed with a client.</param>
    private sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
