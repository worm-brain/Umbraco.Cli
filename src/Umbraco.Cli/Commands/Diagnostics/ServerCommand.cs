using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the read-only <c>server</c> noun (issue #115): status, information, and configuration
/// (single-object GETs) plus troubleshooting (a table of name/value items).
/// </summary>
public static class ServerCommand
{
    /// <summary>Builds the <c>server</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "server",
            "Inspect the Umbraco server's status and configuration."
        ).WithExamples(
            "umbraco server status",
            "umbraco server info",
            "umbraco server troubleshooting"
        );
        cmd.Add(
            DiagnosticsVerb
                .Object(
                    executor,
                    "status",
                    "Show the server's runtime status.",
                    (c, ct) => c.GetServerStatusAsync(ct)
                )
                .WithExamples("umbraco server status", "umbraco server status --output json")
        );
        cmd.Add(
            DiagnosticsVerb
                .Object(
                    executor,
                    "info",
                    "Show server version and runtime-mode information.",
                    (c, ct) => c.GetServerInformationAsync(ct)
                )
                .WithExamples("umbraco server info")
        );
        cmd.Add(
            DiagnosticsVerb
                .Object(
                    executor,
                    "configuration",
                    "Show public server configuration flags.",
                    (c, ct) => c.GetServerConfigurationAsync(ct)
                )
                .WithExamples("umbraco server configuration")
        );
        cmd.Add(BuildTroubleshooting(executor));
        return cmd;
    }

    private static Command BuildTroubleshooting(CommandExecutor executor)
    {
        var cmd = new Command("troubleshooting", "List server troubleshooting items.").WithExamples(
            "umbraco server troubleshooting",
            "umbraco server troubleshooting --output json"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
                    (client, c) => client.GetServerTroubleshootingAsync(c),
                    new[] { "Name", "Data" },
                    i => new[] { i.Name, i.Data },
                    ct
                )
        );
        return cmd;
    }
}
