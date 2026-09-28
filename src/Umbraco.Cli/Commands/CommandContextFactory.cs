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
    /// <exception cref="CommandAbortedException">No host, not authenticated, or blocked by the allow-list.</exception>
    public async Task<CommandContext> CreateAsync(
        ParseResult parseResult,
        CancellationToken ct = default
    )
    {
        // The dotted name comes from the parse tree (never typed by hand), so meta.command and the
        // allow-list always name the command that actually ran.
        var commandName = CommandPath.Of(parseResult) ?? "umbraco";
        var hostOverride = parseResult.GetValue(_globalOptions.Host);
        var tokenOverride = parseResult.GetValue(_globalOptions.Token);

        // --output, --fields (#63) and --quiet, built the same way as for the auth commands.
        var output = _globalOptions.CreateWriter(parseResult);

        var store = ResolveConfigStore(parseResult);

        // Warn when the config file exists but can't be parsed (#83 M1): reads fail open (no
        // profile and, crucially, no file-based allow-list), so surface it on stderr rather than
        // silently dropping a guardrail. Non-fatal — the command still runs on env/flag values.
        if (store.FileExistsButUnreadable())
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
            && store.HasAnyProfiles
            && !store.HasProfile(requestedProfile)
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
        var config = store.Load(profileName);

        // Command allow-list (#69): when configured, only the listed noun groups / commands may
        // run. Checked before auth so a disallowed command fails fast.
        EnforceAllowList(commandName, store, config, profileName, output);

        var host = hostOverride ?? config.Host;
        if (string.IsNullOrEmpty(host))
        {
            // The default profile was logged out of while others remain (#304): say so, rather
            // than suggesting a fresh login when the fix is to pick one of the saved profiles.
            var noDefault =
                string.IsNullOrWhiteSpace(requestedProfile) && store.DefaultProfileMissing;
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
                output.WriteError(
                    ExitCode.Aborted,
                    FailureCategory.NotAuthenticated,
                    $"Authentication failed: {ex.Message}",
                    commandName
                );
                throw new CommandAbortedException();
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
            parseResult.GetValue(_globalOptions.DryRun) ? MutationInterceptPolicy.Preview
            : IsReadOnly(parseResult) ? MutationInterceptPolicy.Block
            : MutationInterceptPolicy.Execute;

        var verbose = parseResult.GetValue(_globalOptions.Verbose);
        var http = _httpClientFactory.CreateClient(verbose ? "umbraco-verbose" : "umbraco");
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
            DryRun = parseResult.GetValue(_globalOptions.DryRun),
            ReadOnly = IsReadOnly(parseResult),
        };
    }

    /// <summary>
    /// Honours <c>--config</c>: when a path is supplied, reads from a store rooted there;
    /// otherwise falls back to the injected store (default path / env vars).
    /// </summary>
    private ConfigStore ResolveConfigStore(ParseResult parseResult) =>
        ConfigStore.Resolve(parseResult.GetValue(_globalOptions.Config), _configStore);

    /// <summary>
    /// Whether read-only mode is active for this invocation: the <c>--readonly</c> flag or a
    /// truthy <c>UMBRACO_READONLY</c> environment variable.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True when writes should be blocked.</returns>
    private bool IsReadOnly(ParseResult parseResult) =>
        parseResult.GetValue(_globalOptions.ReadOnly) || EnvironmentFlags.IsOn("UMBRACO_READONLY");

    /// <summary>
    /// Enforces the command allow-list (#69) for <paramref name="commandName"/>, aborting with exit
    /// code 2 when the command is not permitted.
    /// <para>
    /// The allow-list is a supervisor-set guardrail, so neither <c>--config</c> nor <c>--profile</c>
    /// may be used to LOOSEN it (#83 M2). The decision is the most-restrictive of two allow-lists:
    /// the one from the <em>resolved</em> store/profile the command runs against, and a
    /// <em>baseline</em> from the trusted default store (which also carries any
    /// <c>UMBRACO_ALLOWED_COMMANDS</c> value). The baseline honours the requested profile only when
    /// the default store actually defines it; a profile that exists solely in a <c>--config</c> file
    /// fails closed to the default store's default profile rather than resolving to an empty,
    /// unrestricted config. A command must satisfy both lists, so <c>--config</c> / <c>--profile</c>
    /// can only ever tighten, never bypass.
    /// </para>
    /// </summary>
    /// <param name="commandName">The dotted command name being run, e.g. <c>content.delete</c>.</param>
    /// <param name="store">The store resolved for this invocation (honours <c>--config</c>).</param>
    /// <param name="config">The already-loaded config for the resolved store and requested profile.</param>
    /// <param name="profileName">The requested profile (from <c>--profile</c>), or null for the default.</param>
    /// <param name="output">The output writer used to report a refusal.</param>
    /// <exception cref="CommandAbortedException">Thrown when the command is not in the allow-list.</exception>
    private void EnforceAllowList(
        string commandName,
        ConfigStore store,
        CliConfig config,
        string? profileName,
        IOutputWriter output
    )
    {
        var resolvedAllowList = config.AllowedCommands;

        string? baselineAllowList;
        if (ReferenceEquals(store, _configStore))
        {
            // No --config: the resolved store IS the trusted default store, so this is one check.
            baselineAllowList = resolvedAllowList;
        }
        else
        {
            // --config is in play. Take the baseline from the default store, trusting the requested
            // profile only when the default store actually defines it; otherwise fall back to the
            // default store's default profile so a --config-only profile cannot dodge the guardrail
            // by resolving to an empty (unrestricted) config (#83 M2, profile axis).
            var baselineProfile =
                profileName is not null && !_configStore.HasProfile(profileName)
                    ? null
                    : profileName;
            baselineAllowList = _configStore.Load(baselineProfile).AllowedCommands;
        }

        if (
            !IsCommandAllowed(commandName, baselineAllowList)
            || !IsCommandAllowed(commandName, resolvedAllowList)
        )
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
    /// (e.g. <c>content.list</c>). The <c>auth</c> group is always permitted so the session can
    /// authenticate and be inspected.
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
        // An entry written before a rename (#268) still names the same commands, and no more.
        return entries
            .Select(LegacyNames.Canonical)
            .Any(entry =>
                string.Equals(entry, group, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry, commandName, StringComparison.OrdinalIgnoreCase)
            );
    }
}
