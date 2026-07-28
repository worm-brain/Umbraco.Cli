using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-types update</c> command (issue #59).</summary>
public static class DataTypesUpdateCommand
{
    /// <summary>
    /// Builds the <c>data-types update</c> command. Only supplied options change; anything
    /// omitted (including the editor configuration values, which are never exposed here) is
    /// preserved by the client's read-merge.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a data type by UUID. Omitted fields (and editor configuration) are preserved.\n\nExample:\n  umbraco data-types update 3f7a8b2e-... --name \"My Text\""
        );
        var idArg = new Argument<Guid>("id") { Description = "Data type ID." };
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var editorAliasOpt = new Option<string?>("--editor-alias")
        {
            Description = "New backend property editor alias.",
        };
        var editorUiAliasOpt = new Option<string?>("--editor-ui-alias")
        {
            Description = "New backoffice editor UI alias.",
        };
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.Add(editorAliasOpt);
        cmd.Add(editorUiAliasOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "data-types.update",
                    (client, c) =>
                        client.UpdateDataTypeAsync(
                            parseResult.GetValue(idArg),
                            new UpdateDataTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                EditorAlias = parseResult.GetValue(editorAliasOpt),
                                EditorUiAlias = parseResult.GetValue(editorUiAliasOpt),
                            },
                            c
                        ),
                    "Data type updated.",
                    ct
                )
        );

        return cmd;
    }
}
