using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the <c>health</c> noun (issue #115): list the health-check groups, inspect a group's
/// checks, and run a group. <c>run</c> executes the group's checks and returns their results; it is
/// diagnostic (not a persistent mutation), so it is not confirmation-gated.
/// </summary>
public static class HealthCommand
{
    /// <summary>Builds the <c>health</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "health",
            "List, inspect, and run Umbraco health-check groups."
        ).WithExamples("umbraco health list", "umbraco health run \"Data Integrity\"");
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildRun(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List the health-check groups.").WithExamples(
            "umbraco health list",
            "umbraco health list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetHealthCheckGroupsAsync(skip, take, c),
                    new[] { "Name" },
                    g => new[] { g.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a health-check group and the checks it contains."
        ).WithExamples("umbraco health get \"Data Integrity\"", "umbraco health get Security");
        var nameArg = new Argument<string>("name") { Description = "Health-check group name." };
        cmd.Add(nameArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.GetHealthCheckGroupAsync(parseResult.GetValue(nameArg)!, c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildRun(CommandExecutor executor)
    {
        // A POST: it runs the checks server-side, so --readonly blocks it and the catalog says so.
        var cmd = new Command("run", "Run a health-check group and show the results.")
            .WithExamples(
                "umbraco health run \"Data Integrity\"",
                "umbraco health run Security --output json"
            )
            .Mutating();
        var nameArg = new Argument<string>("name") { Description = "Health-check group name." };
        cmd.Add(nameArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.RunHealthCheckGroupAsync(parseResult.GetValue(nameArg)!, c),
                    ct
                )
        );
        return cmd;
    }
}
