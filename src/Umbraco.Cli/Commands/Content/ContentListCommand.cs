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
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetContentAsync(parseResult.GetValue(parentOpt), skip, take, c),
                    // Content Type is intentionally omitted: the document-tree list items carry
                    // only the type id (no alias), so the column was always blank (#75). Use
                    // 'content get <id>' for the full content type.
                    ["ID", "Name", "Published"],
                    i => new[] { i.Id.ToString(), i.Name, i.IsPublished.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
