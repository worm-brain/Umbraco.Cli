using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all data types (property editors) configured in the Umbraco instance.\n\nEach item on the page is read individually so it can carry its editorAlias, which is what decides a property's value format - so this costs one request per item returned. Use --take to bound it.\n\nExamples:\n  umbraco data-type list --take 50\n  umbraco data-type list --parent <folder-id>"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        // #247: there was no way to see what a data-type folder holds.
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Folder id; lists only the data types directly inside it.",
        };
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetDataTypesAsync(skip, take, parseResult.GetValue(parentOpt), c),
                    // #176: editorAlias is what decides a property's value shape (#174), so the
                    // list carries it even though the tree items do not - the client hydrates
                    // each one. See the note in the command description about the cost.
                    ["ID", "Name", "Editor Alias"],
                    i => new[] { i.Id.ToString(), i.Name, i.EditorAlias },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
