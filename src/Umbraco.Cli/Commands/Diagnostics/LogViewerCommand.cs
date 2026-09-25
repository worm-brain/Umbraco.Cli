using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the <c>log-viewer</c> noun (issue #115): log messages (with level/date/filter options),
/// logger levels, per-level counts, message templates, and a <c>saved-search</c> sub-noun
/// (list/create/delete). Deleting a saved search is confirmation-gated.
/// </summary>
public static class LogViewerCommand
{
    /// <summary>Builds the <c>log-viewer</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "log-viewer",
            "Query the Umbraco logs.\n\nExamples:\n  umbraco log-viewer list --level Error --take 50\n  umbraco log-viewer level-count"
        );
        cmd.Add(BuildLog(executor));
        cmd.Add(BuildLevels(executor));
        cmd.Add(BuildLevelCount(executor));
        cmd.Add(BuildMessageTemplates(executor));
        cmd.Add(BuildSavedSearch(executor));
        return cmd;
    }

    private static Command BuildLog(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List log messages, optionally filtered by level/date/expression.\n\nExamples:\n  umbraco log-viewer list --level Error --take 50\n  umbraco log-viewer list --start-date 2026-09-01 --end-date 2026-09-02 --asc\n  umbraco log-viewer list --filter \"@Level='Error' and Has(@Exception)\""
        );
        var levelOpt = ListOption.Enums<LogLevel>(
            "--level",
            "Filter by level (Verbose/Debug/Information/Warning/Error/Fatal)."
        );
        var filterOpt = new Option<string?>("--filter")
        {
            Description = "A filter expression applied to the messages.",
        };
        var startOpt = new Option<DateTimeOffset?>("--start-date")
        {
            Description = "Only messages on/after this date.",
        };
        var endOpt = new Option<DateTimeOffset?>("--end-date")
        {
            Description = "Only messages on/before this date.",
        };
        var ascendingOpt = new Option<bool>("--asc")
        {
            Description = "Order oldest-first (default is newest-first).",
        };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.Add(levelOpt);
        cmd.Add(filterOpt);
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.Add(ascendingOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetLogsAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            parseResult.GetValue(levelOpt),
                            parseResult.GetValue(filterOpt),
                            parseResult.GetValue(startOpt),
                            parseResult.GetValue(endOpt),
                            descending: !parseResult.GetValue(ascendingOpt),
                            c
                        ),
                    new[] { "Timestamp", "Level", "Message" },
                    m => new[] { m.Timestamp.ToString("u"), m.Level ?? "", m.RenderedMessage },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildLevels(CommandExecutor executor)
    {
        var cmd = new Command(
            "levels",
            "List the configured loggers and their minimum levels.\n\nExamples:\n  umbraco log-viewer levels"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetLogLevelsAsync(skip, take, c),
                    new[] { "Name", "Level" },
                    l => new[] { l.Name, l.Level ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildLevelCount(CommandExecutor executor)
    {
        var cmd = new Command(
            "level-count",
            "Show message counts by level over a date range.\n\nExamples:\n  umbraco log-viewer level-count\n  umbraco log-viewer level-count --start-date 2026-09-01 --end-date 2026-09-08"
        );
        var startOpt = new Option<DateTimeOffset?>("--start-date") { Description = "Range start." };
        var endOpt = new Option<DateTimeOffset?>("--end-date") { Description = "Range end." };
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.GetLogLevelCountsAsync(
                            parseResult.GetValue(startOpt),
                            parseResult.GetValue(endOpt),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildMessageTemplates(CommandExecutor executor)
    {
        var cmd = new Command(
            "message-templates",
            "List the most common message templates.\n\nExamples:\n  umbraco log-viewer message-templates --take 20\n  umbraco log-viewer message-templates --start-date 2026-09-01"
        );
        var startOpt = new Option<DateTimeOffset?>("--start-date") { Description = "Range start." };
        var endOpt = new Option<DateTimeOffset?>("--end-date") { Description = "Range end." };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetLogMessageTemplatesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            parseResult.GetValue(startOpt),
                            parseResult.GetValue(endOpt),
                            c
                        ),
                    new[] { "Count", "MessageTemplate" },
                    t => new[] { t.Count.ToString(), t.MessageTemplate },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildSavedSearch(CommandExecutor executor)
    {
        var cmd = new Command("saved-search", "Manage saved log searches.");
        cmd.Add(BuildSavedSearchList(executor));
        cmd.Add(BuildSavedSearchCreate(executor));
        cmd.Add(BuildSavedSearchDelete(executor));
        return cmd;
    }

    private static Command BuildSavedSearchList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List saved log searches.\n\nExamples:\n  umbraco log-viewer saved-search list"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetSavedLogSearchesAsync(skip, take, c),
                    new[] { "Name", "Query" },
                    s => new[] { s.Name, s.Query },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildSavedSearchCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a saved log search.\n\nExamples:\n  umbraco log-viewer saved-search create --name Errors --query \"@Level='Error'\""
        ).Mutating();
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Search name.",
        };
        var queryOpt = new Option<string>("--query")
        {
            Required = true,
            Description = "The query to save.",
        };
        cmd.Add(nameOpt);
        cmd.Add(queryOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.CreateSavedLogSearchAsync(
                            parseResult.GetValue(nameOpt)!,
                            parseResult.GetValue(queryOpt)!,
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildSavedSearchDelete(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a saved log search by name.\n\nExamples:\n  umbraco log-viewer saved-search delete Errors --yes"
        ).Mutating();
        var nameArg = new Argument<string>("name") { Description = "Saved search name." };
        cmd.Add(nameArg);
        cmd.Destructive(parseResult => $"Delete saved search '{parseResult.GetValue(nameArg)}'?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteSavedLogSearchAsync(parseResult.GetValue(nameArg)!, c)
                            .Then(ItemRef.Of(parseResult.GetValue(nameArg))),
                    "Saved search deleted.",
                    ct
                )
        );
        return cmd;
    }
}
