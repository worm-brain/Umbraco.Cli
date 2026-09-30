using System.CommandLine;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The recursive options available on every command. Held once and shared so that
/// <see cref="CommandContextFactory"/> can read them off any subcommand's
/// <see cref="ParseResult"/> — no need to thread the option instances through every
/// command's <c>Build</c> signature.
/// </summary>
public sealed class GlobalOptions
{
    /// <summary>The values <c>--output</c> accepts, in the order tab completion offers them.</summary>
    public static readonly IReadOnlyList<string> OutputFormats = ["json", "human", "csv"];

    /// <summary>
    /// Creates the global options. <c>--output</c> gets its values as completions (#379), so
    /// <c>umbraco -o &lt;TAB&gt;</c> offers them instead of file names, and a validator that
    /// refuses any other value at parse time (#392): <c>-o yaml</c> is an
    /// <c>invalid_argument</c> parse error naming the valid formats, reported before the command
    /// runs or sends a request, rather than a silent fallback to the default format. The value
    /// is still matched case-insensitively by <see cref="OutputFormatParser"/>.
    /// </summary>
    public GlobalOptions()
    {
        Output.CompletionSources.Add([.. OutputFormats]);
        Output.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && OutputFormatParser.Parse(value) is null)
                result.AddError(
                    $"'{value}' is not a valid --output format: expected one of "
                        + $"{string.Join(", ", OutputFormats)}."
                );
        });
    }

    public Option<string?> Host { get; } =
        new("--host", new[] { "-H" })
        {
            Description = "Umbraco instance base URL (overrides config / UMBRACO_HOST).",
            Recursive = true,
        };

    public Option<string?> Token { get; } =
        new("--token")
        {
            Description =
                "Raw bearer token (overrides credential store). Also settable with UMBRACO_TOKEN.",
            Recursive = true,
        };

    public Option<string?> Output { get; } =
        new("--output", new[] { "-o" })
        {
            Description =
                "Output format: json | human | csv (default: json when piped, human in terminal). "
                + "Also settable with UMBRACO_OUTPUT.",
            Recursive = true,
        };

    public Option<bool> Quiet { get; } =
        new("--quiet", new[] { "-q" })
        {
            Description =
                "Print nothing when a write succeeds (no confirmation, no result). Reads still "
                + "print their data; errors, dry-run previews and exit codes are unaffected.",
            Recursive = true,
        };

    public Option<bool> Verbose { get; } =
        new("--verbose", new[] { "-v" })
        {
            Description =
                "Write each HTTP request and response to stderr: method, URL, headers, status, the request body and the first 4 KB of the response body. Secrets are redacted; binary bodies are summarised.",
            Recursive = true,
        };

    public Option<bool> DryRun { get; } =
        new("--dry-run")
        {
            Description =
                "Preview the HTTP requests a write command would send (method, URL, body; secrets "
                + "redacted) without executing them. No effect on read commands. Can also be set "
                + "with UMBRACO_DRY_RUN=1.",
            Recursive = true,
        };

    public Option<bool> Yes { get; } =
        new("--yes", new[] { "-y" })
        {
            Description =
                "Skip the confirmation prompt on destructive commands (delete). Required to "
                + "run a destructive command non-interactively (piped/scripted/agent).",
            Recursive = true,
        };

    public Option<bool> ReadOnly { get; } =
        new("--readonly")
        {
            Description =
                "Block all write operations (create/update/delete/publish) for this session. "
                + "Can also be set with UMBRACO_READONLY=1.",
            Recursive = true,
        };

    public Option<string?> Fields { get; } =
        new("--fields")
        {
            Description =
                "Comma-separated fields to keep in JSON output (e.g. id,name), in that order. "
                + "Trims each result to those top-level fields to keep output small.",
            Recursive = true,
        };

    public Option<string?> Config { get; } =
        new("--config")
        {
            Description =
                $"Path to config file (default: {ConfigStore.DefaultConfigPath}). "
                + "Also settable with UMBRACO_CONFIG.",
            Recursive = true,
        };

    public Option<string?> Profile { get; } =
        new("--profile", new[] { "-p" })
        {
            Description =
                "Named credential profile to use (see 'auth profile list'). "
                + "Also settable with UMBRACO_PROFILE. Defaults to the configured default profile.",
            Recursive = true,
        };

    // ── Environment defaults (ADR 0010) ───────────────────────────────────────
    //
    // An extension command's calls back into the CLI run in the context of the command line that
    // launched it. The launcher passes the context options it was given as these variables, and
    // every command reads them as the option's default: an explicit option on the command line
    // still wins. --profile, --readonly and --host already had variables (UMBRACO_PROFILE,
    // UMBRACO_READONLY, UMBRACO_HOST), read where they always were.

    /// <summary>The variable read as the default of <c>--config</c>.</summary>
    public const string ConfigVariable = "UMBRACO_CONFIG";

    /// <summary>The variable read as the default of <c>--token</c>.</summary>
    public const string TokenVariable = "UMBRACO_TOKEN";

    /// <summary>The variable read as the default of <c>--output</c>.</summary>
    public const string OutputVariable = "UMBRACO_OUTPUT";

    /// <summary>The variable that turns <c>--dry-run</c> on, like <c>UMBRACO_READONLY</c> does <c>--readonly</c>.</summary>
    public const string DryRunVariable = "UMBRACO_DRY_RUN";

    /// <summary>The variable that turns <c>--readonly</c> on.</summary>
    public const string ReadOnlyVariable = "UMBRACO_READONLY";

    /// <summary>The config file path: <c>--config</c>, else <c>UMBRACO_CONFIG</c>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The path, or null for the default config file.</returns>
    public string? ConfigPath(ParseResult parseResult) =>
        parseResult.GetValue(Config) ?? FromEnvironment(ConfigVariable);

    /// <summary>The caller's own bearer token: <c>--token</c>, else <c>UMBRACO_TOKEN</c>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The token, or null to use the configured client credentials.</returns>
    public string? TokenOf(ParseResult parseResult) =>
        parseResult.GetValue(Token) ?? FromEnvironment(TokenVariable);

    /// <summary>
    /// The output format asked for: <c>--output</c>, else <c>UMBRACO_OUTPUT</c>. An unrecognised
    /// variable value is ignored, as if unset; an unrecognised option value never gets here (the
    /// validator refuses it at parse time).
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The format, or null to decide by whether stdout is a terminal.</returns>
    public OutputFormat? FormatOf(ParseResult parseResult) =>
        OutputFormatParser.Parse(parseResult.GetValue(Output))
        ?? OutputFormatParser.Parse(FromEnvironment(OutputVariable));

    /// <summary>Whether writes are previewed rather than sent: <c>--dry-run</c> or a truthy <c>UMBRACO_DRY_RUN</c>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True under a dry run.</returns>
    public bool IsDryRun(ParseResult parseResult) =>
        parseResult.GetValue(DryRun) || EnvironmentFlags.IsOn(DryRunVariable);

    /// <summary>Whether writes are refused: <c>--readonly</c> or a truthy <c>UMBRACO_READONLY</c>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True in read-only mode.</returns>
    public bool IsReadOnly(ParseResult parseResult) =>
        parseResult.GetValue(ReadOnly) || EnvironmentFlags.IsOn(ReadOnlyVariable);

    /// <summary>A variable's value, or null when it is unset or blank.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or null.</returns>
    private static string? FromEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>
    /// Builds the output writer a command asked for: <c>--output</c>, <c>--fields</c> and
    /// <c>--quiet</c>. Every command builds its writer here, including the <c>auth</c> commands
    /// that run without a <see cref="CommandContext"/>, so the global output options behave the
    /// same everywhere (#305). Under <c>--quiet</c> a write (a command declared mutating) drops
    /// its whole result, unless it is a <c>--dry-run</c> preview (#347) or the write declared its
    /// result report data with <see cref="CommandSafety.ReportsResult{TCommand}"/> (#393).
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The writer.</returns>
    public IOutputWriter CreateWriter(ParseResult parseResult) =>
        OutputWriterFactory.Create(
            FormatOf(parseResult),
            ParseFields(parseResult.GetValue(Fields)),
            parseResult.GetValue(Quiet),
            isWrite: CommandSafety.QuietDropsResult(parseResult.CommandResult.Command)
                && !IsDryRun(parseResult)
        );

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

    /// <summary>Every global option, in the order help lists them.</summary>
    public IReadOnlyList<Option> All =>
        [Host, Token, Output, Quiet, Verbose, DryRun, Yes, ReadOnly, Fields, Config, Profile];

    /// <summary>
    /// The options that say where and how requests run, which an extension command's line hands
    /// to its calls back into the CLI (ADR 0010) rather than to the extension. The rest of
    /// <see cref="All"/> (<c>--yes</c>, <c>--quiet</c>, <c>--verbose</c>, <c>--fields</c>) are
    /// passed to the extension as arguments, for it to act on or forward.
    /// </summary>
    public IReadOnlyList<Option> Context =>
        [Host, Token, Output, DryRun, ReadOnly, Config, Profile];

    /// <summary>Adds every global option to the supplied (root) command.</summary>
    /// <param name="command">The root command.</param>
    public void AddTo(Command command)
    {
        foreach (var option in All)
            command.Add(option);
    }
}
