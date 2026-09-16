using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the read-only <c>server</c> noun (issue #115): status, information, configuration,
/// troubleshooting, and the upgrade check. All verbs are single GETs against the diagnostics API.
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
            Object(
                executor,
                "status",
                "Show the server's runtime status.",
                "server.status",
                (c, ct) => c.GetServerStatusAsync(ct)
            )
        );
        cmd.Add(
            Object(
                executor,
                "info",
                "Show server version and runtime-mode information.",
                "server.info",
                (c, ct) => c.GetServerInformationAsync(ct)
            )
        );
        cmd.Add(
            Object(
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

    /// <summary>Builds a no-argument verb that renders a single object from a client call.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="name">The verb name.</param>
    /// <param name="description">The verb help text.</param>
    /// <param name="commandName">The telemetry/command name.</param>
    /// <param name="call">The client call.</param>
    /// <returns>The configured verb command.</returns>
    private static Command Object<T>(
        CommandExecutor executor,
        string name,
        string description,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call
    )
    {
        var cmd = new Command(name, description);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    commandName,
                    (client, c) => call(client, c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildTroubleshooting(CommandExecutor executor)
    {
        var cmd = new Command("troubleshooting", "List server troubleshooting items.");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "server.troubleshooting",
                    (client, c) => client.GetServerTroubleshootingAsync(c),
                    new[] { "Name", "Data" },
                    data => (data ?? []).Select(i => new[] { i.Name, i.Data }),
                    ct
                )
        );
        return cmd;
    }
}
