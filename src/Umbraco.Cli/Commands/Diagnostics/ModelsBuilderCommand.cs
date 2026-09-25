using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
        cmd.Add(
            DiagnosticsVerb.Object(
                executor,
                "dashboard",
                "Show the models-builder dashboard status.",
                "models-builder.dashboard",
                (c, ct) => c.GetModelsBuilderDashboardAsync(ct)
            )
        );
        cmd.Add(
            DiagnosticsVerb.Object(
                executor,
                "status",
                "Show whether the generated models are out of date.",
                "models-builder.status",
                (c, ct) => c.GetModelsBuilderStatusAsync(ct)
            )
        );
        cmd.Add(BuildBuild(executor));
        return cmd;
    }

    private static Command BuildBuild(CommandExecutor executor)
    {
        var cmd = new Command(
            "build",
            "Regenerate the models (writes source files on the server)."
        );
        cmd.Destructive(parseResult =>
            "Regenerate models on the server (overwrites generated source files)?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "models-builder.build",
                    (client, c) => client.BuildModelsAsync(c),
                    "Models build triggered.",
                    ct
                )
        );
        return cmd;
    }
}
