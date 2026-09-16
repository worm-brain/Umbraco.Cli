using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the <c>models-builder</c> noun (issue #115): read the dashboard/status, and trigger a
/// build. <c>build</c> regenerates model source files on the server (a genuine mutation), so it is
/// confirmation-gated.
/// </summary>
public static class ModelsBuilderCommand
{
    /// <summary>Builds the <c>models-builder</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "models-builder",
            "Inspect and trigger the Umbraco models builder.\n\nExamples:\n  umbraco models-builder dashboard\n  umbraco models-builder build --yes"
        );
        cmd.Add(BuildDashboard(executor));
        cmd.Add(BuildStatus(executor));
        cmd.Add(BuildBuild(executor));
        return cmd;
    }

    private static Command BuildDashboard(CommandExecutor executor)
    {
        var cmd = new Command("dashboard", "Show the models-builder dashboard status.");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "models-builder.dashboard",
                    (client, c) => client.GetModelsBuilderDashboardAsync(c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildStatus(CommandExecutor executor)
    {
        var cmd = new Command("status", "Show whether the generated models are out of date.");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "models-builder.status",
                    (client, c) => client.GetModelsBuilderStatusAsync(c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildBuild(CommandExecutor executor)
    {
        var cmd = new Command(
            "build",
            "Regenerate the models (writes source files on the server)."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "models-builder.build",
                    (client, c) => client.BuildModelsAsync(c),
                    "Models build triggered.",
                    ct,
                    confirmationPrompt: "Regenerate models on the server (overwrites generated source files)?"
                )
        );
        return cmd;
    }
}
