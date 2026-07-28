using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-types delete</c> command (issue #59).</summary>
public static class DataTypesDeleteCommand
{
    /// <summary>Builds the <c>data-types delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a data type by UUID.\n\nExample:\n  umbraco data-types delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Data type ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "data-types.delete",
                    (client, c) => client.DeleteDataTypeAsync(parseResult.GetValue(idArg), c),
                    "Data type deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete data type {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
