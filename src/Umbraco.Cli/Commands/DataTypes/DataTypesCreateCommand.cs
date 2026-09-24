using System.CommandLine;
using System.Text.Json.Nodes;
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
        var body = RawBodyCommand.AddBodyOptions(cmd);
        var nameOpt = new Option<string>("--name");
        var editorAliasOpt = new Option<string>("--editor-alias")
        {
            Description = "Backend property editor alias (e.g. Umbraco.TextBox).",
        };
        var editorUiAliasOpt = new Option<string>("--editor-ui-alias")
        {
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

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result) || body.HasBody(result))
                return;
            if (
                string.IsNullOrEmpty(result.GetValue(nameOpt))
                || string.IsNullOrEmpty(result.GetValue(editorAliasOpt))
                || string.IsNullOrEmpty(result.GetValue(editorUiAliasOpt))
            )
                result.AddError(
                    "Supply --name, --editor-alias and --editor-ui-alias, or a full body with "
                        + "--json-body. Run with --schema to print a real data type as a starting point."
                );
        });
        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        "data-types.create",
                        (client, c) =>
                            RawBodyCommand.ExampleAsync(
                                client,
                                client.GetDataTypeIdsAsync,
                                client.GetDataTypeRawAsync,
                                "data types",
                                c
                            ),
                        ct
                    );

                if (body.HasBody(parseResult))
                    return executor.RunMessageAsync(
                        parseResult,
                        "data-types.create",
                        async (client, c) =>
                            await client.CreateDataTypeRawAsync(
                                JsonNode.Parse(await body.ReadAsync(parseResult, c))
                                    ?? throw new InvalidOperationException("Invalid JSON body."),
                                c
                            ),
                        "Data type created.",
                        ct
                    );

                return executor.RunObjectAsync(
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
                );
            }
        );

        return cmd;
    }
}
