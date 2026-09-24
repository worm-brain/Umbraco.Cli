using System.CommandLine;
using Umbraco.Cli.Client;

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
            "Inspect the Umbraco server's status and configuration.\n\nExamples:\n  umbraco server status\n  umbraco server info\n  umbraco server troubleshooting"
        );
        cmd.Add(
            DiagnosticsVerb.Object(
                executor,
                "status",
                "Show the server's runtime status.",
                "server.status",
                (c, ct) => c.GetServerStatusAsync(ct)
            )
        );
        cmd.Add(
            DiagnosticsVerb.Object(
                executor,
                "info",
                "Show server version and runtime-mode information.",
                "server.info",
                (c, ct) => c.GetServerInformationAsync(ct)
            )
        );
        cmd.Add(
            DiagnosticsVerb.Object(
                executor,
                "configuration",
                "Show public server configuration flags.",
                "server.configuration",
                (c, ct) => c.GetServerConfigurationAsync(ct)
            )
        );
        cmd.Add(BuildTroubleshooting(executor));
        return cmd;
    }

    private static Command BuildTroubleshooting(CommandExecutor executor)
    {
        var cmd = new Command("troubleshooting", "List server troubleshooting items.");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
                    "server.troubleshooting",
                    (client, c) => client.GetServerTroubleshootingAsync(c),
                    new[] { "Name", "Data" },
                    i => new[] { i.Name, i.Data },
                    ct
                )
        );
        return cmd;
    }
}
