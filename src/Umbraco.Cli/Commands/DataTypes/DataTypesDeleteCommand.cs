using System.CommandLine;
using Umbraco.Cli.Infrastructure;

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
            "Delete a data type by UUID.\n\n"
                + "A data type that is in use is refused unless --force is given: Umbraco deletes "
                + "every property that uses it, and all the values in those properties, with it.\n\n"
                + "Example:\n  umbraco data-types delete 3f7a8b2e-... --yes"
        );
        var idArg = new Argument<Guid>("id") { Description = "Data type ID." };
        var forceOpt = new Option<bool>(InUseGuard.ForceOption)
        {
            Description =
                "Delete even though the data type is in use, removing the properties that use it and their values.",
        };
        cmd.Add(idArg);
        cmd.Add(forceOpt);
        cmd.Destructive(parseResult =>
            $"Permanently delete data type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "data-types.delete",
                    async (client, c) =>
                    {
                        // #246: check before deleting - Umbraco cascades an in-use delete.
                        var id = parseResult.GetValue(idArg);
                        InUseGuard.Refuse(
                            await InUseGuard.DataTypeAsync(client, id, c),
                            parseResult.GetValue(forceOpt)
                        );
                        return await client.DeleteDataTypeAsync(id, c);
                    },
                    "Data type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
