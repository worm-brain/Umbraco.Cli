using System.CommandLine;
using System.Text.Json.Nodes;
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
        var idArg = new Argument<string?>("id")
        {
            Description = "Data type name or UUID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var body = RawBodyCommand.AddBodyOptions(cmd);
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

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (string.IsNullOrEmpty(result.GetValue(idArg)))
                result.AddError(
                    "Supply the data type name or id. "
                        + "Run with --schema to print a real data type as a starting point."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        "data-types.update",
                        (client, c) =>
                            RawBodyCommand.ExampleAsync(
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
                        "data-types.update",
                        async (client, c) =>
                        {
                            var resolved = await client.GetDataTypeAsync(
                                parseResult.GetValue(idArg)!,
                                c
                            );
                            if (!resolved.IsSuccess)
                                return UmbracoResponse<Empty>.Failure(
                                    resolved.StatusCode,
                                    resolved.ErrorMessage!,
                                    resolved.Category
                                );

                            return await client.UpdateDataTypeRawAsync(
                                resolved.Data!.Id,
                                JsonNode.Parse(await body.ReadAsync(parseResult, c))
                                    ?? throw new InvalidOperationException("Invalid JSON body."),
                                c
                            );
                        },
                        "Data type updated.",
                        ct
                    );

                return executor.RunMessageAsync(
                    parseResult,
                    "data-types.update",
                    async (client, c) =>
                    {
                        var resolved = await client.GetDataTypeAsync(
                            parseResult.GetValue(idArg)!,
                            c
                        );
                        if (!resolved.IsSuccess)
                            return UmbracoResponse<Empty>.Failure(
                                resolved.StatusCode,
                                resolved.ErrorMessage!,
                                resolved.Category
                            );

                        return await client.UpdateDataTypeAsync(
                            resolved.Data!.Id,
                            new UpdateDataTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                EditorAlias = parseResult.GetValue(editorAliasOpt),
                                EditorUiAlias = parseResult.GetValue(editorUiAliasOpt),
                            },
                            c
                        );
                    },
                    "Data type updated.",
                    ct
                );
            }
        );

        return cmd;
    }
}
