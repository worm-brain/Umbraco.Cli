using System.CommandLine;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The recursive options available on every command. Held once and shared so that
/// <see cref="CommandContextFactory"/> can read them off any subcommand's
/// <see cref="ParseResult"/> — no need to thread the option instances through every
/// command's <c>Build</c> signature.
/// </summary>
public sealed class GlobalOptions
{
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
                "Suppress success confirmations (e.g. \"Deleted.\"). Requested data, errors, and "
                + "exit codes are unaffected.",
            Recursive = true,
        };

    public Option<bool> Verbose { get; } =
        new("--verbose", new[] { "-v" })
        {
            Description =
                "Write the HTTP method, URL, selected headers and status to stderr. Request and response bodies are NOT included - use --dry-run to see the request body.",
            Recursive = true,
        };

    public Option<bool> DryRun { get; } =
        new("--dry-run")
        {
            Description =
                "Preview the HTTP request a write command would send (method, URL, body) "
                + "without executing it. No effect on read commands.",
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
                "Named credential profile to use (see 'auth profiles'). "
                + "Also settable with UMBRACO_PROFILE. Defaults to the configured default profile.",
            Recursive = true,
        };

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
