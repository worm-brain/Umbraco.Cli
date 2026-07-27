using System.CommandLine;
using System.Net.Http.Headers;
using Umbraco.Cli.Client;
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

    public CommandContextFactory(
        ConfigStore configStore,
        UmbracoAuthService authService,
        IHttpClientFactory httpClientFactory,
        GlobalOptions globalOptions,
        IUmbracoManagementClientFactory clientFactory,
        MutationInterceptState mutationState
    )
    {
        _configStore = configStore;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _globalOptions = globalOptions;
        _clientFactory = clientFactory;
        _mutationState = mutationState;
    }

    /// <summary>
    /// Builds the context for a command from the parsed global options. Throws
    /// <see cref="CommandAbortedException"/> (after writing the error) when no host is
    /// configured or the caller is not authenticated.
    /// </summary>
    public async Task<CommandContext> CreateAsync(
        ParseResult parseResult,
        string commandName,
        CancellationToken ct = default
    )
    {
        var hostOverride = parseResult.GetValue(_globalOptions.Host);
        var tokenOverride = parseResult.GetValue(_globalOptions.Token);
        var outputFormat = OutputFormatParser.Parse(parseResult.GetValue(_globalOptions.Output));

        // --fields projection (#63): split the comma-separated list once and hand it to the
        // JSON writer, which trims each result to those fields.
        var fields = ParseFields(parseResult.GetValue(_globalOptions.Fields));
        var output = OutputWriterFactory.Create(outputFormat, fields);
        var config = ResolveConfigStore(parseResult).Load();

        // Command allow-list (#69): when configured, only the listed noun groups / commands may
        // run. Checked before auth so a disallowed command fails fast. The `auth` group is
        // always allowed — you need it to authenticate and inspect the session.
        if (!IsCommandAllowed(commandName, config.AllowedCommands))
        {
            output.WriteError(
                2,
                $"Command '{commandName}' is not in the allow-list. Set UMBRACO_ALLOWED_COMMANDS "
                    + "(or the config 'allowedCommands') to include its group or full name."
            );
            throw new CommandAbortedException();
        }

        var host = hostOverride ?? config.Host;
        if (string.IsNullOrEmpty(host))
        {
            output.WriteError(
                2,
                "No Umbraco host configured. Run 'umbraco auth login' or set UMBRACO_HOST."
            );
            throw new CommandAbortedException();
        }

        string bearerToken;
        if (!string.IsNullOrEmpty(tokenOverride))
        {
            bearerToken = tokenOverride;
        }
        else
        {
            if (!config.IsComplete)
            {
                output.WriteError(
                    2,
                    "Not authenticated. Run 'umbraco auth login' or set UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET."
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
                output.WriteError(2, $"Authentication failed: {ex.Message}");
                throw new CommandAbortedException();
            }
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
    /// Splits the <c>--fields</c> value into a trimmed, non-empty field list (#63), or null when
    /// nothing usable was supplied.
    /// </summary>
    /// <param name="raw">The raw comma-separated option value.</param>
    /// <returns>The field names, or null for no projection.</returns>
    private static string[]? ParseFields(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var fields = raw.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToArray();
        return fields.Length > 0 ? fields : null;
    }

    /// <summary>
    /// Whether read-only mode is active for this invocation: the <c>--readonly</c> flag or a
    /// truthy <c>UMBRACO_READONLY</c> environment variable.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True when writes should be blocked.</returns>
    private bool IsReadOnly(ParseResult parseResult) =>
        parseResult.GetValue(_globalOptions.ReadOnly)
        || IsTruthy(Environment.GetEnvironmentVariable("UMBRACO_READONLY"));

    /// <summary>Whether an environment-variable value should be read as "on" (1/true/yes).</summary>
    /// <param name="value">The raw environment value.</param>
    /// <returns>True for a truthy value.</returns>
    private static bool IsTruthy(string? value)
    {
        // Trim so a stray trailing space (easy to introduce in a Windows `set VAR=1 `) does not
        // silently disable the guardrail.
        var v = value?.Trim();
        return !string.IsNullOrEmpty(v)
            && (
                v == "1"
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            );
    }

    /// <summary>
    /// Whether <paramref name="commandName"/> is permitted by the allow-list (#69). An empty
    /// allow-list permits everything. An entry matches either the command's noun group
    /// (e.g. <c>content</c>) or its full name (e.g. <c>content.list</c>). The <c>auth</c> group
    /// is always permitted so the session can authenticate and be inspected.
    /// </summary>
    /// <param name="commandName">The dotted command name, e.g. <c>content.delete</c>.</param>
    /// <param name="allowedRaw">The comma-separated allow-list, or null/empty for no restriction.</param>
    /// <returns>True if the command may run.</returns>
    private static bool IsCommandAllowed(string commandName, string? allowedRaw)
    {
        if (string.IsNullOrWhiteSpace(allowedRaw))
            return true;

        var group = commandName.Split('.', 2)[0];
        if (string.Equals(group, "auth", StringComparison.OrdinalIgnoreCase))
            return true;

        var entries = allowedRaw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return entries.Any(entry =>
            string.Equals(entry, group, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, commandName, StringComparison.OrdinalIgnoreCase)
        );
    }
}
