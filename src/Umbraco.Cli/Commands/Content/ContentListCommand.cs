using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

public static class ContentListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List content items at the root, or the children of a parent.\n\nReturns a paginated list of top-level or child content nodes.\n\nExamples:\n  umbraco content list\n  umbraco content list --parent <id> --take 50\n  umbraco content list --output json | jq '.data[].name'"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent content item id; lists root items if omitted.",
        };
        cmd.Add(parentOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);

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
