using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "data-types",
            "List and inspect Umbraco data types (property editors).\n\nExample:\n  umbraco data-types list --output json"
        );
        cmd.Add(DataTypesListCommand.Build(executor));
        cmd.Add(DataTypesGetCommand.Build(executor));
        return cmd;
    }
}
