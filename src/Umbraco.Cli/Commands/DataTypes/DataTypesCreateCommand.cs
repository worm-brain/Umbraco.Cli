using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires the <c>data-types create</c> command (issue #59).</summary>
public static class DataTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>data-types create</c> command. A data type wraps a property editor:
    /// <c>--editor-alias</c> is the backend editor and <c>--editor-ui-alias</c> the backoffice
    /// UI. Editor configuration values are not exposed here (default to empty).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a data type (property editor configuration).\n\nExample:\n  umbraco data-types create --name \"My Text\" --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox"
        );
        var nameOpt = new Option<string>("--name") { Required = true };
        var editorAliasOpt = new Option<string>("--editor-alias")
        {
            Required = true,
            Description = "Backend property editor alias (e.g. Umbraco.TextBox).",
        };
        var editorUiAliasOpt = new Option<string>("--editor-ui-alias")
        {
            Required = true,
            Description = "Backoffice editor UI alias (e.g. Umb.PropertyEditorUi.TextBox).",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(nameOpt);
        cmd.Add(editorAliasOpt);
        cmd.Add(editorUiAliasOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "data-types.create",
                    (client, c) =>
                        client.CreateDataTypeAsync(
                            new CreateDataTypeRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
                                EditorAlias = parseResult.GetValue(editorAliasOpt)!,
                                EditorUiAlias = parseResult.GetValue(editorUiAliasOpt)!,
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
