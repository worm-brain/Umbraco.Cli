using System.CommandLine;
using Umbraco.Cli.Client;

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
            "Query the Umbraco logs.\n\nExamples:\n  umbraco log-viewer log --level Error --take 50\n  umbraco log-viewer level-count"
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
            "log",
            "List log messages, optionally filtered by level/date/expression."
        );
        var levelOpt = new Option<LogLevel[]>("--level")
        {
            AllowMultipleArgumentsPerToken = true,
            Description =
                "Filter by level (Verbose/Debug/Information/Warning/Error/Fatal); repeat for several.",
        };
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
        var ascendingOpt = new Option<bool>("--ascending")
        {
            Description = "Order oldest-first (default is newest-first).",
        };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.Add(levelOpt);
        cmd.Add(filterOpt);
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.Add(ascendingOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "log-viewer.log",
                    (client, c) =>
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
        var cmd = new Command("levels", "List the configured loggers and their minimum levels.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "log-viewer.levels",
                    (client, c) =>
                        client.GetLogLevelsAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
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
        var cmd = new Command("level-count", "Show message counts by level over a date range.");
        var startOpt = new Option<DateTimeOffset?>("--start-date") { Description = "Range start." };
        var endOpt = new Option<DateTimeOffset?>("--end-date") { Description = "Range end." };
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "log-viewer.level-count",
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
        var cmd = new Command("message-templates", "List the most common message templates.");
        var startOpt = new Option<DateTimeOffset?>("--start-date") { Description = "Range start." };
        var endOpt = new Option<DateTimeOffset?>("--end-date") { Description = "Range end." };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.Add(startOpt);
        cmd.Add(endOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "log-viewer.message-templates",
                    (client, c) =>
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
        var cmd = new Command("list", "List saved log searches.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "log-viewer.saved-search.list",
                    (client, c) =>
                        client.GetSavedLogSearchesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
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
        var cmd = new Command("create", "Create a saved log search.");
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
                    "log-viewer.saved-search.create",
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
        var cmd = new Command("delete", "Delete a saved log search by name.");
        var nameArg = new Argument<string>("name") { Description = "Saved search name." };
        cmd.Add(nameArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "log-viewer.saved-search.delete",
                    (client, c) =>
                        client.DeleteSavedLogSearchAsync(parseResult.GetValue(nameArg)!, c),
                    "Saved search deleted.",
                    ct,
                    confirmationPrompt: $"Delete saved search '{parseResult.GetValue(nameArg)}'?"
                )
        );
        return cmd;
    }
}
