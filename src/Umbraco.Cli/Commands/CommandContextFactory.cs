using System.CommandLine;
using System.Net.Http.Headers;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

public sealed class CommandContextFactory
{
    private readonly ConfigStore _configStore;
    private readonly UmbracoAuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GlobalOptions _globalOptions;
    private readonly IUmbracoManagementClientFactory _clientFactory;
    private readonly MutationInterceptState _mutationState;
    private readonly TokenRefreshState _tokenRefresh;

    /// <summary>Creates the factory.</summary>
    /// <param name="configStore">The default config store.</param>
    /// <param name="authService">Exchanges client credentials for tokens.</param>
    /// <param name="httpClientFactory">Creates the Management API client.</param>
    /// <param name="globalOptions">The recursive global options.</param>
    /// <param name="clientFactory">Wraps the HTTP client in the management client.</param>
    /// <param name="mutationState">The per-run dry-run/read-only policy.</param>
    /// <param name="tokenRefresh">The per-run 401 refresh state (#248); a private one when omitted, as in tests with no handler pipeline.</param>
    public CommandContextFactory(
        ConfigStore configStore,
        UmbracoAuthService authService,
        IHttpClientFactory httpClientFactory,
        GlobalOptions globalOptions,
        IUmbracoManagementClientFactory clientFactory,
        MutationInterceptState mutationState,
        TokenRefreshState? tokenRefresh = null
    )
    {
        _configStore = configStore;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _globalOptions = globalOptions;
        _clientFactory = clientFactory;
        _mutationState = mutationState;
        _tokenRefresh = tokenRefresh ?? new TokenRefreshState();
    }

    /// <summary>
    /// Builds the context for a command from the parsed global options. Throws
    /// <see cref="CommandAbortedException"/> (after writing the error) when no host is
    /// configured or the caller is not authenticated.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The built context.</returns>
    /// <exception cref="CommandAbortedException">
    /// No host, not authenticated, blocked by the allow-list, a plain-HTTP non-loopback host, or a
    /// <c>--host</c> the configured credentials are not bound to (see <see cref="UsesCredentialsForOtherHost"/>),
    /// all with <see cref="ExitCode.Aborted"/>; or a token exchange that got no response, with
    /// <see cref="ExitCode.Failed"/> (see <see cref="WriteAuthFailure"/>).
    /// </exception>
    public async Task<CommandContext> CreateAsync(
        ParseResult parseResult,
        CancellationToken ct = default
    )
    {
        // The dotted name comes from the parse tree (never typed by hand), so meta.command and the
        // allow-list always name the command that actually ran.
        var commandName = CommandPath.Of(parseResult) ?? "umbraco";
        var hostOverride = parseResult.GetValue(_globalOptions.Host);
        var tokenOverride = _globalOptions.TokenOf(parseResult);

        // --output, --fields (#63) and --quiet, built the same way as for the auth commands.
        var output = _globalOptions.CreateWriter(parseResult);

        // Each config file is read and parsed once per command (#425): every question below is
        // asked of these snapshots. The default store's allow-lists apply under --config too.
        var store = ResolveConfigStore(parseResult);
        var file = store.Read();
        var defaultConfig = ReferenceEquals(store, _configStore) ? file : _configStore.Read();

        // Warn when the config file exists but can't be parsed (#83 M1): reads fail open (no
        // profile and, crucially, no file-based allow-list), so surface it on stderr rather than
        // silently dropping a guardrail. Non-fatal — the command still runs on env/flag values.
        if (file.FileExistsButUnreadable)
            Console.Error.WriteLine(
                "warning: the Umbraco config file exists but could not be read; it is being "
                    + "ignored (using --host/--token/UMBRACO_* values instead). Any credentials "
                    + "or command allow-list stored in it will not apply."
            );

        // Fail fast on an unknown profile (#64): if a profile was explicitly requested (via
        // --profile or UMBRACO_PROFILE) and the config defines profiles but not that one, abort
        // with a clear error rather than silently resolving to empty credentials — which would
        // also drop a file-based allow-list (#69) when env credentials are present.
        var requestedProfile =
            parseResult.GetValue(_globalOptions.Profile)
            ?? Environment.GetEnvironmentVariable("UMBRACO_PROFILE");
        if (
            !string.IsNullOrWhiteSpace(requestedProfile)
            && file.HasAnyProfiles
            && !file.HasProfile(requestedProfile)
        )
        {
            output.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAuthenticated,
                $"No profile named '{requestedProfile}'. See 'umbraco auth profile list'.",
                commandName
            );
            throw new CommandAbortedException();
        }

        // Resolve the selected profile (#64): --profile flag, else UMBRACO_PROFILE / the
        // configured default.
        var profileName = parseResult.GetValue(_globalOptions.Profile);
        var config = file.Load(profileName);

        // Command allow-list (#69): when configured, only the listed noun groups / commands may
        // run. Checked before auth so a disallowed command fails fast.
        EnforceAllowList(commandName, defaultConfig, file, output);

        var host = hostOverride ?? config.Host;
        if (string.IsNullOrEmpty(host))
        {
            // The default profile was logged out of while others remain (#304): say so, rather
            // than suggesting a fresh login when the fix is to pick one of the saved profiles.
            var noDefault =
                string.IsNullOrWhiteSpace(requestedProfile) && file.DefaultProfileMissing;
            output.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAuthenticated,
                noDefault
                    ? "No default profile is set (it was logged out of). Run 'umbraco auth profile use <name>', or pass --profile."
                    : "No Umbraco host configured. Run 'umbraco auth login' or set UMBRACO_HOST.",
                commandName
            );
            throw new CommandAbortedException();
        }

        // Credentials (a client secret, or the bearer every request carries) never travel over
        // plain HTTP to a non-loopback host.
        if (HostPolicy.InsecureTransportError(host) is { } insecure)
            Abort(output, insecure, commandName);

        // Stored and environment credentials are bound to the host they were configured with.
        // A --host naming another instance may only be used with a --token given alongside it,
        // otherwise the configured secret would be sent to whatever host the caller names.
        if (UsesCredentialsForOtherHost(hostOverride, tokenOverride, config))
            Abort(output, CredentialHostMismatchMessage(hostOverride!), commandName);

        string bearerToken;
        if (!string.IsNullOrEmpty(tokenOverride))
        {
            bearerToken = tokenOverride;
            // A --token is the caller's; there is nothing to renew it with.
            _tokenRefresh.Reset(bearerToken, null);
        }
        else
        {
            if (!config.IsComplete)
            {
                output.WriteError(
                    ExitCode.Aborted,
                    FailureCategory.NotAuthenticated,
                    "Not authenticated. Run 'umbraco auth login' or set UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET.",
                    commandName
                );
                throw new CommandAbortedException();
            }
            try
            {
                bearerToken = await _authService.GetTokenAsync(
                    host,
                    config.ClientId!,
                    config.ClientSecret!,
                    ct
                );
            }
            catch (UmbracoAuthException ex)
            {
                throw new CommandAbortedException(WriteAuthFailure(output, ex, commandName));
            }

            // A cached token can be rejected before it expires (#248): drop it and exchange the
            // credentials again, from the HTTP pipeline, for the one request that got the 401.
            var (clientId, clientSecret) = (config.ClientId!, config.ClientSecret!);
            _tokenRefresh.Reset(
                bearerToken,
                c =>
                {
                    _authService.Invalidate(host, clientId, clientSecret);
                    return _authService.GetTokenAsync(host, clientId, clientSecret, c);
                }
            );
        }

        // Set the interception policy for this invocation (after auth, so the OAuth token
        // exchange itself still runs). Set explicitly (not just on the "on" cases) so a
        // reused/hosted state — this is the shared seam for #62/#69/#70 — can never carry a
        // stale policy. --dry-run wins over --readonly: a preview sends nothing, so it is
        // harmless and still useful in a read-only session.
        _mutationState.Policy =
            _globalOptions.IsDryRun(parseResult) ? MutationInterceptPolicy.Preview
            : IsReadOnly(parseResult) ? MutationInterceptPolicy.Block
            : MutationInterceptPolicy.Execute;
        _mutationState.Previewed.Clear();

        // --verbose logging is switched on by VerboseState (set in Program.cs), so the one client
        // serves both modes, as the token exchange's client does (#374).
        var http = _httpClientFactory.CreateClient("umbraco");
        http.BaseAddress = new Uri(host.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            bearerToken
        );

        return new CommandContext
        {
            Output = output,
            Client = _clientFactory.Create(http),
            CommandName = commandName,
            AssumeYes = parseResult.GetValue(_globalOptions.Yes),
            DryRun = _globalOptions.IsDryRun(parseResult),
            ReadOnly = IsReadOnly(parseResult),
            Previewed = _mutationState.Previewed,
        };
    }

    /// <summary>
    /// Whether this invocation would send the configured client credentials to a host other than
    /// the one they were configured for: a <c>--host</c> is given, no <c>--token</c> replaces the
    /// credentials, a client secret is configured, and the override names a different host than
    /// the resolved profile / <c>UMBRACO_HOST</c> value.
    /// </summary>
    /// <param name="hostOverride">The <c>--host</c> value, or null.</param>
    /// <param name="tokenOverride">The <c>--token</c> value, or null.</param>
    /// <param name="config">The resolved profile and environment configuration.</param>
    /// <returns>True when the credentials must not be used.</returns>
    internal static bool UsesCredentialsForOtherHost(
        string? hostOverride,
        string? tokenOverride,
        CliConfig config
    ) =>
        hostOverride is not null
        && string.IsNullOrEmpty(tokenOverride)
        && !string.IsNullOrEmpty(config.ClientSecret)
        && !HostPolicy.IsSameHost(hostOverride, config.Host);

    /// <summary>The error for a <c>--host</c> that the configured credentials are not bound to.</summary>
    /// <param name="hostOverride">The <c>--host</c> value.</param>
    /// <returns>The message, naming the ways to target that host.</returns>
    internal static string CredentialHostMismatchMessage(string hostOverride) =>
        $"--host '{hostOverride}' is not the host the configured credentials belong to, so they "
        + "will not be sent there. Pass --token with --host, use a profile logged in to that host "
        + "('umbraco auth login --host <url> --profile <name>'), or set UMBRACO_HOST with its "
        + "own UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET.";

    /// <summary>
    /// Writes the error for a failed client-credentials token exchange and returns the exit code
    /// it was written with. Shared by every command's context and by <c>auth login</c>, so one fact
    /// is reported one way whichever command hit it (#445).
    /// <list type="bullet">
    /// <item>
    /// The credentials were refused (<c>not_authenticated</c>): exit 2, as a command that is not
    /// authenticated.
    /// </item>
    /// <item>
    /// A plain-HTTP host (<c>refused</c>, #450): exit 2, as every command reports that rule.
    /// </item>
    /// <item>
    /// Anything else is the site's side, as for any other request (#445, #449): no response
    /// (<c>unreachable</c>, <c>timeout</c>), a 5xx (<c>server_error</c>) or an answer the CLI can't
    /// use (<c>unexpected_response</c>). Exit 1, and the message says nothing about the
    /// credentials, which were never judged.
    /// </item>
    /// </list>
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="ex">The token exchange's failure.</param>
    /// <param name="commandName">The dotted command name, for <c>meta.command</c>.</param>
    /// <returns>The exit code to return.</returns>
    internal static ExitCode WriteAuthFailure(
        IOutputWriter output,
        UmbracoAuthException ex,
        string? commandName
    )
    {
        var (exitCode, message) = ex.Category switch
        {
            FailureCategory.NotAuthenticated => (
                ExitCode.Aborted,
                $"Authentication failed: {ex.Message}"
            ),
            FailureCategory.Refused => (ExitCode.Aborted, ex.Message),
            _ => (ExitCode.Failed, ex.Message),
        };
        output.WriteError(exitCode, ex.Category, message, commandName);
        return exitCode;
    }

    /// <summary>
    /// Writes a refusal (exit code 2, category <c>refused</c>) and aborts the command.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="message">The error message.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <exception cref="CommandAbortedException">Always.</exception>
    private static void Abort(IOutputWriter output, string message, string commandName)
    {
        output.WriteError(ExitCode.Aborted, FailureCategory.Refused, message, commandName);
        throw new CommandAbortedException();
    }

    /// <summary>
    /// Honours <c>--config</c>: when a path is supplied, reads from a store rooted there;
    /// otherwise falls back to the injected store (default path / env vars).
    /// </summary>
    private ConfigStore ResolveConfigStore(ParseResult parseResult) =>
        ConfigStore.Resolve(_globalOptions.ConfigPath(parseResult), _configStore);

    /// <summary>Whether read-only mode is active for this invocation; see <see cref="GlobalOptions.IsReadOnly"/>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True when writes should be blocked.</returns>
    private bool IsReadOnly(ParseResult parseResult) => _globalOptions.IsReadOnly(parseResult);

    /// <summary>
    /// The checks an extension command gets before it is launched (ADR 0010), which has no
    /// <see cref="CommandContext"/> of its own because it may need no host at all.
    /// <list type="bullet">
    /// <item>The allow-list, with the extension's noun as its group, exactly as a built-in noun.</item>
    /// <item>
    /// The credential-host rule for a <c>--host</c> given without <c>--token</c>. The launcher
    /// passes that host to the extension as <c>UMBRACO_HOST</c>, which the extension's own calls
    /// cannot tell from a configured host, so the rule a <c>--host</c> flag gets is applied here
    /// instead, before it becomes one.
    /// </item>
    /// </list>
    /// Everything else (no host, not authenticated, plain HTTP) is checked by the extension's own
    /// calls back into the CLI, which build a context as usual.
    /// </summary>
    /// <param name="noun">The extension's noun, e.g. <c>foo</c> for <c>umbraco-foo</c>.</param>
    /// <param name="configPath">The <c>--config</c> the extension was given, or null.</param>
    /// <param name="profile">The <c>--profile</c> the extension was given, or null.</param>
    /// <param name="hostOverride">The <c>--host</c> the extension was given, or null.</param>
    /// <param name="tokenOverride">The <c>--token</c> the extension was given, or null.</param>
    /// <param name="output">The writer a refusal is reported through.</param>
    /// <returns>Null to launch it; otherwise the exit code, after the error has been written.</returns>
    public int? RefuseExtension(
        string noun,
        string? configPath,
        string? profile,
        string? hostOverride,
        string? tokenOverride,
        IOutputWriter output
    )
    {
        var store = ConfigStore.Resolve(configPath, _configStore);
        var file = store.Read();
        var defaultConfig = ReferenceEquals(store, _configStore) ? file : _configStore.Read();
        try
        {
            EnforceAllowList(noun, defaultConfig, file, output);
            if (UsesCredentialsForOtherHost(hostOverride, tokenOverride, file.Load(profile)))
                Abort(output, CredentialHostMismatchMessage(hostOverride!), noun);
            return null;
        }
        catch (CommandAbortedException)
        {
            return (int)ExitCode.Aborted;
        }
    }

    /// <summary>
    /// Enforces the command allow-list (#69) for <paramref name="commandName"/>, aborting with exit
    /// code 2 when the command is not permitted.
    /// <para>
    /// The allow-list is a supervisor-set guardrail, so nothing a session chooses for itself may
    /// LOOSEN it (#83 M2, SEC-PRIV-002). Every list in force is checked and a command must satisfy
    /// all of them: <c>UMBRACO_ALLOWED_COMMANDS</c>, every profile's list in the trusted default
    /// store and, under <c>--config</c>, every profile's list in that file too. File lists are
    /// file-wide rather than tied to the selected profile, so <c>--profile</c>,
    /// <c>UMBRACO_PROFILE</c>, <c>auth profile use</c>, a newly logged-in profile, <c>--config</c>
    /// and the environment variable can each only ever tighten, never bypass.
    /// </para>
    /// </summary>
    /// <param name="commandName">The dotted command name being run, e.g. <c>content.delete</c>.</param>
    /// <param name="defaultConfig">The trusted default store's file, as read for this invocation.</param>
    /// <param name="config">
    /// The file of the store resolved for this invocation (honours <c>--config</c>); the same
    /// snapshot as <paramref name="defaultConfig"/> without <c>--config</c>.
    /// </param>
    /// <param name="output">The output writer used to report a refusal.</param>
    /// <exception cref="CommandAbortedException">Thrown when the command is not in the allow-list.</exception>
    private static void EnforceAllowList(
        string commandName,
        ConfigStore.Snapshot defaultConfig,
        ConfigStore.Snapshot config,
        IOutputWriter output
    )
    {
        // Gather every list in force. The env value is read directly (not through Load, where it
        // replaces the profile's list) so it adds to the file lists instead of overriding them.
        var lists = new List<string?>
        {
            Environment.GetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS"),
        };
        lists.AddRange(defaultConfig.AllowLists());
        if (!ReferenceEquals(config, defaultConfig))
            lists.AddRange(config.AllowLists());

        if (lists.Any(list => !IsCommandAllowed(commandName, list)))
        {
            output.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAllowed,
                $"Command '{commandName}' is not in the allow-list. Set UMBRACO_ALLOWED_COMMANDS "
                    + "(or the config 'allowedCommands') to include its group or full name.",
                commandName
            );
            throw new CommandAbortedException();
        }
    }

    /// <summary>
    /// Whether <paramref name="commandName"/> is permitted by the allow-list (#69). A <c>null</c>
    /// allow-list means none was configured anywhere, so nothing is restricted. An allow-list that
    /// is present but empty or whitespace (e.g. <c>UMBRACO_ALLOWED_COMMANDS=" "</c> or a config
    /// <c>allowedCommands</c> of <c>","</c>) is an <em>explicit lockdown</em>: it permits nothing but
    /// the always-allowed <c>auth</c> group (#83 L3). This removes the earlier asymmetry where an
    /// empty string meant "allow everything" while <c>","</c> meant "deny everything". A non-empty
    /// entry matches either the command's noun group (e.g. <c>content</c>) or its full name
    /// (e.g. <c>content.list</c>), compared case-insensitively against the current command names
    /// only: an entry naming a pre-#268 noun (e.g. <c>content-types</c>) matches nothing (#272).
    /// The <c>auth</c> group is always permitted so the session can authenticate and be inspected.
    /// </summary>
    /// <param name="commandName">The dotted command name, e.g. <c>content.delete</c>.</param>
    /// <param name="allowedRaw">The comma-separated allow-list; <c>null</c> for no restriction, or set-but-empty for an explicit lockdown.</param>
    /// <returns>True if the command may run.</returns>
    private static bool IsCommandAllowed(string commandName, string? allowedRaw)
    {
        // Only a truly absent (null) allow-list means "unrestricted". A present-but-blank value is
        // a deliberate lockdown and falls through to the entry check below, which yields no entries
        // and so permits only the auth group.
        if (allowedRaw is null)
            return true;

        var group = commandName.Split('.', 2)[0];
        if (string.Equals(group, "auth", StringComparison.OrdinalIgnoreCase))
            return true;

        var entries = allowedRaw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        // Entries match current names only: the pre-#268 plural nouns were removed in #272, so an
        // old entry such as `content-types` matches nothing rather than being translated.
        return entries.Any(entry =>
            string.Equals(entry, group, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, commandName, StringComparison.OrdinalIgnoreCase)
        );
    }
}
