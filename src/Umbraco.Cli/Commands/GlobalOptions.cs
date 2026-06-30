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
                "Output format: json | human (default: json when piped, human in terminal).",
            Recursive = true,
        };

    public Option<bool> Verbose { get; } =
        new("--verbose", new[] { "-v" })
        {
            Description = "Write HTTP request/response details to stderr.",
            Recursive = true,
        };

    public Option<string?> Config { get; } =
        new("--config")
        {
            Description = $"Path to config file (default: {ConfigStore.DefaultConfigPath}).",
            Recursive = true,
        };

    /// <summary>Adds every global option to the supplied (root) command.</summary>
    public void AddTo(Command command)
    {
        command.Add(Host);
        command.Add(Token);
        command.Add(Output);
        command.Add(Verbose);
        command.Add(Config);
    }
}
