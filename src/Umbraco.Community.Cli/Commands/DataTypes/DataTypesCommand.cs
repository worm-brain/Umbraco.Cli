using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-type</c> noun: configured property editors.</summary>
public static class DataTypesCommand
{
    /// <summary>Builds the <c>data-type</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "data-type",
            "List, inspect, and manage Umbraco data types (property editors)."
        ).WithExamples(
            "umbraco data-type list --output json",
            "umbraco data-type create --name \"My Text\" --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox"
        );
        cmd.Add(DataTypesListCommand.Build(executor));
        cmd.Add(DataTypesGetCommand.Build(executor));
        cmd.Add(DataTypesCreateCommand.Build(executor));
        cmd.Add(DataTypesUpdateCommand.Build(executor));
        cmd.Add(DataTypesDeleteCommand.Build(executor));
        // Advanced verbs (#121).
        cmd.Add(DataTypesAdvancedCommands.BuildIsUsed(executor));
        cmd.Add(DataTypesAdvancedCommands.BuildReferencedBy(executor));
        cmd.Add(DataTypesAdvancedCommands.BuildCopy(executor));
        cmd.Add(DataTypesAdvancedCommands.BuildMove(executor));
        cmd.Add(DataTypesAdvancedCommands.BuildFolder(executor));
        return cmd;
    }
}
