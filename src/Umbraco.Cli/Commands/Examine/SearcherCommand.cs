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
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 100 };
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "searcher.list",
                    (client, c) =>
                        client.GetSearchersAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Name" },
                    data => (data?.Items ?? []).Select(s => new[] { s.Name }),
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
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(nameArg);
        cmd.Add(termOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "searcher.query",
                    (client, c) =>
                        client.QuerySearcherAsync(
                            parseResult.GetValue(nameArg)!,
                            parseResult.GetValue(termOpt)!,
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Id", "Score" },
                    data =>
                        (data?.Items ?? []).Select(r => new[] { r.Id, r.Score.ToString("0.###") }),
                    ct
                )
        );
        return cmd;
    }
}
