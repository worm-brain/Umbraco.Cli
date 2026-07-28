using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "data-types",
            "List, inspect, and manage Umbraco data types (property editors).\n\nExamples:\n  umbraco data-types list --output json\n  umbraco data-types create --name \"My Text\" --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox"
        );
        cmd.Add(DataTypesListCommand.Build(executor));
        cmd.Add(DataTypesGetCommand.Build(executor));
        cmd.Add(DataTypesCreateCommand.Build(executor));
        cmd.Add(DataTypesUpdateCommand.Build(executor));
        cmd.Add(DataTypesDeleteCommand.Build(executor));
        return cmd;
    }
}
