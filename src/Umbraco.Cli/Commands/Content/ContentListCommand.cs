using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

public static class ContentListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List content items. Returns a paginated list of top-level or child content nodes.\n\nExamples:\n  umbraco content list\n  umbraco content list --parent <id> --take 50\n  umbraco content list --output json | jq '.data[].name'"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Filter by parent content item ID (UUID). Omit for root items.",
        };
        var skipOpt = new Option<int>("--skip")
        {
            DefaultValueFactory = _ => 0,
            Description = "Number of items to skip for pagination.",
        };
        var takeOpt = new Option<int>("--take")
        {
            DefaultValueFactory = _ => 20,
            Description = "Maximum number of items to return.",
        };
        cmd.Add(parentOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "content.list",
                    (client, c) =>
                        client.GetContentAsync(
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Content Type", "Published"],
                    data =>
                        (data?.Items ?? []).Select(i =>
                            new[]
                            {
                                i.Id.ToString(),
                                i.Name,
                                i.ContentType?.Alias ?? "",
                                i.IsPublished.ToString(),
                            }
                        ),
                    ct
                )
        );

        return cmd;
    }
}
