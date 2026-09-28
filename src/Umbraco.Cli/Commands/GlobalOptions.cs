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
            Description = "Raw bearer token (overrides credential store).",
            Recursive = true,
        };

    public Option<string?> Output { get; } =
        new("--output", new[] { "-o" })
        {
            Description =
                "Output format: json | human | csv (default: json when piped, human in terminal).",
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
                + "redacted) without executing them. No effect on read commands.",
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
            Description = $"Path to config file (default: {ConfigStore.DefaultConfigPath}).",
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

    /// <summary>
    /// Builds the output writer a command asked for: <c>--output</c>, <c>--fields</c> and
    /// <c>--quiet</c>. Every command builds its writer here, including the <c>auth</c> commands
    /// that run without a <see cref="CommandContext"/>, so the global output options behave the
    /// same everywhere (#305). Under <c>--quiet</c> a write (a command declared mutating) drops
    /// its whole result, unless it is a <c>--dry-run</c> preview (#347).
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The writer.</returns>
    public IOutputWriter CreateWriter(ParseResult parseResult) =>
        OutputWriterFactory.Create(
            OutputFormatParser.Parse(parseResult.GetValue(Output)),
            ParseFields(parseResult.GetValue(Fields)),
            parseResult.GetValue(Quiet),
            isWrite: CommandSafety.IsDeclaredMutating(parseResult.CommandResult.Command)
                && !parseResult.GetValue(DryRun)
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

    /// <summary>Adds every global option to the supplied (root) command.</summary>
    public void AddTo(Command command)
    {
        command.Add(Host);
        command.Add(Token);
        command.Add(Output);
        command.Add(Quiet);
        command.Add(Verbose);
        command.Add(DryRun);
        command.Add(Yes);
        command.Add(ReadOnly);
        command.Add(Fields);
        command.Add(Config);
        command.Add(Profile);
    }
}
