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
            "List Examine searchers and query them.\n\nExamples:\n  umbraco searcher query ExternalIndex --term news"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildQuery(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List the registered Examine multi-searchers.\n\n"
                + "This is often empty (it is on Umbraco 17): every index can still be queried by its "
                + "name - see 'umbraco indexer list'.\n\n"
                + "Examples:\n  umbraco searcher list"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
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
        var cmd = new Command(
            "query",
            "Query a searcher, or an index, for a term.\n\n"
                + "Give an index name from 'umbraco indexer list' (e.g. ExternalIndex), or its "
                + "searcherName (ExternalSearcher), which is mapped to the index when Umbraco does "
                + "not register it as a searcher.\n\n"
                + "Examples:\n  umbraco searcher query ExternalIndex --term Docker\n  umbraco searcher query ExternalSearcher --term news --take 10"
        );
        var nameArg = new Argument<string>("name")
        {
            Description = "An index name, or a searcher name.",
        };
        var termOpt = new Option<string>("--term")
        {
            Required = true,
            Description = "The query term.",
        };
        cmd.Add(nameArg);
        cmd.Add(termOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
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
