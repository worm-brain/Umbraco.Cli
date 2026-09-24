using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Examine;

/// <summary>
/// Wires the read-only <c>searcher</c> noun (issue #121): list Examine searchers and query one.
/// </summary>
public static class SearcherCommand
{
    /// <summary>Builds the <c>searcher</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "searcher",
            "List Examine searchers and query them.\n\nExample:\n  umbraco searcher query ExternalSearcher --term news"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildQuery(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List the Examine searchers.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "searcher.list",
                    (client, skip, take, c) => client.GetSearchersAsync(skip, take, c),
                    new[] { "Name" },
                    s => new[] { s.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildQuery(CommandExecutor executor)
    {
        var cmd = new Command("query", "Query a searcher for a term.");
        var nameArg = new Argument<string>("name") { Description = "Searcher name." };
        var termOpt = new Option<string>("--term")
        {
            Required = true,
            Description = "The query term.",
        };
        cmd.Add(nameArg);
        cmd.Add(termOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "searcher.query",
                    (client, skip, take, c) =>
                        client.QuerySearcherAsync(
                            parseResult.GetValue(nameArg)!,
                            parseResult.GetValue(termOpt)!,
                            skip,
                            take,
                            c
                        ),
                    new[] { "Id", "Score" },
                    r => new[] { r.Id, r.Score.ToString("0.###") },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }
}
