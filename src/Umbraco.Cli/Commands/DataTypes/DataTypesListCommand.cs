using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List all data types (property editors) configured in the Umbraco instance.\n\nExample:\n  umbraco data-types list");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction((parseResult, ct) => executor.RunTableAsync(
            parseResult, "data-types.list",
            (client, c) => client.GetDataTypesAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), c),
            ["ID", "Name", "Editor Alias"],
            data => data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.EditorAlias }) ?? [],
            ct));

        return cmd;
    }
}
