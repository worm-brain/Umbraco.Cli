using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-types delete</c> command (issue #59).</summary>
public static class DataTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>data-types delete</c> command: destructive, gated by confirmation, and
    /// refused before confirmation while the data type is in use, unless <c>--force</c> (#246).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a data type by id or name.\n\n"
                + "A data type that is in use is refused unless --force is given: Umbraco deletes "
                + "every property that uses it, and all the values in those properties, with it.\n\n"
                + "Example:\n  umbraco data-types delete \"Homepage Blocks\" --yes"
        );
        var idArg = Reference.Argument(EntityKind.DataType);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            SchemaKinds.DataType,
            idArg,
            "Delete even though the data type is in use, removing the properties that use it and their values."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete data type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "data-types.delete",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteDataTypeAsync(id, c),
                            c
                        ),
                    "Data type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
