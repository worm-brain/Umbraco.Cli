using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all document types defined in the Umbraco instance.\n\nExamples:\n  umbraco content-types list\n  umbraco content-types list --output json | jq '.[].alias'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "content-types.list",
                    (client, skip, take, c) => client.GetDocumentTypesAsync(skip, take, c),
                    // Alias is intentionally omitted: the document-type tree list items don't
                    // carry an alias, so the column was always blank (#75). Use
                    // 'content-types get <id|alias>' for the full alias.
                    ["ID", "Name", "IsElement"],
                    i => new[] { i.Id.ToString(), i.Name, i.IsElement.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
