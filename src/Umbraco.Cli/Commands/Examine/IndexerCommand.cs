using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Examine;

/// <summary>
/// Wires the <c>indexer</c> noun (issue #121): list Examine indexes, inspect one, and rebuild.
/// <c>rebuild</c> is an expensive server-side action, so it is confirmation-gated.
/// </summary>
public static class IndexerCommand
{
    /// <summary>Builds the <c>indexer</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "indexer",
            "List, inspect, and rebuild Examine indexes.\n\nExamples:\n  umbraco indexer list\n  umbraco indexer rebuild ExternalIndex --yes"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildRebuild(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List the Examine indexes.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetIndexersAsync(skip, take, c),
                    new[] { "Name", "Health", "Documents", "CanRebuild" },
                    i =>
                        new[]
                        {
                            i.Name,
                            i.HealthStatus ?? "",
                            i.DocumentCount.ToString(),
                            i.CanRebuild.ToString(),
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get an index by name.");
        var nameArg = new Argument<string>("name") { Description = "Index name." };
        cmd.Add(nameArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetIndexerAsync(parseResult.GetValue(nameArg)!, c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildRebuild(CommandExecutor executor)
    {
        var cmd = new Command("rebuild", "Rebuild an index by name (expensive).");
        var nameArg = new Argument<string>("name") { Description = "Index name." };
        cmd.Add(nameArg);
        cmd.Destructive(parseResult =>
            $"Rebuild index '{parseResult.GetValue(nameArg)}'? This can be expensive."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.RebuildIndexAsync(parseResult.GetValue(nameArg)!, c),
                    "Index rebuild triggered.",
                    ct
                )
        );
        return cmd;
    }
}
