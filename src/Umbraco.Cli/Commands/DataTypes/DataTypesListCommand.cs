using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all data types (property editors) configured in the Umbraco instance.\n\nExample:\n  umbraco data-types list"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "data-types.list",
                    (client, c) =>
                        client.GetDataTypesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    // The data-type tree list carries the editor UI alias, not the backend
                    // editor alias (which is always blank in the list, #75). Show the UI alias;
                    // use 'data-types get <id>' for the full backend editor alias.
                    ["ID", "Name", "Editor UI Alias"],
                    data =>
                        data?.Items.Select(i =>
                            new[] { i.Id.ToString(), i.Name, i.EditorUiAlias ?? "" }
                        )
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
