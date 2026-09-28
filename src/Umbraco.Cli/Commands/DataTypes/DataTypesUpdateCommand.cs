using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-type update</c> command (issue #59).</summary>
public static class DataTypesUpdateCommand
{
    /// <summary>
    /// Builds the <c>data-type update</c> command. Only supplied options change; anything
    /// omitted (including the editor configuration values, which are never exposed here) is
    /// preserved by the client's read-merge.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a data type by name or id.\n\nOmitted fields (and editor configuration) are preserved. With --json-body, the body's top-level keys are merged into the type; --replace sends it as the whole type.\n\nExamples:\n  umbraco data-type update Textstring --name \"My Text\"\n  umbraco data-type update Textstring --json-body dt.json"
        ).Mutating();
        var options = RawBodyCommand.AddUpdateOptions(cmd, SchemaNoun.DataTypes, hasFlags: true);
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var editorAliasOpt = new Option<string?>("--editor-alias")
        {
            Description = "New backend property editor alias.",
        };
        var editorUiAliasOpt = new Option<string?>("--editor-ui-alias")
        {
            Description = "New backoffice editor UI alias.",
        };
        cmd.Add(nameOpt);
        cmd.Add(editorAliasOpt);
        cmd.Add(editorUiAliasOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunUpdateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.DataTypes,
                    options,
                    "Data type updated.",
                    (client, id, c) =>
                        client.UpdateDataTypeAsync(
                            id,
                            new UpdateDataTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                EditorAlias = parseResult.GetValue(editorAliasOpt),
                                EditorUiAlias = parseResult.GetValue(editorUiAliasOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
