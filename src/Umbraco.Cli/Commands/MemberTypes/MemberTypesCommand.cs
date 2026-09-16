using System.CommandLine;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types</c> command group (issue #56).</summary>
public static class MemberTypesCommand
{
    /// <summary>Builds the <c>member-types</c> noun with its list/get/create/update/delete verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "member-types",
            "List, inspect, and manage Umbraco member types.\n\nExamples:\n  umbraco member-types list\n  umbraco member-types get 3f7a8b2e-...\n  umbraco member-types create --name \"Author\" --alias author"
        );
        cmd.Add(MemberTypesListCommand.Build(executor));
        cmd.Add(MemberTypesGetCommand.Build(executor));
        cmd.Add(MemberTypesCreateCommand.Build(executor));
        cmd.Add(MemberTypesUpdateCommand.Build(executor));
        cmd.Add(MemberTypesDeleteCommand.Build(executor));
        return cmd;
    }
}
