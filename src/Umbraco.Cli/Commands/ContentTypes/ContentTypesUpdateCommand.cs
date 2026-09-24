using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>
/// Wires <c>content-types update</c> (#161).
/// <para>
/// There was no update verb at all, so changing a document type - adding a property, a group, a
/// template, or turning on culture variance - meant exporting the whole schema, editing it with
/// <c>jq</c>, and applying it back. This takes the type's own body directly.
/// </para>
/// </summary>
public static class ContentTypesUpdateCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a document type from a full Management API body - its properties, groups, compositions, allowed templates and culture variance.\n\nThe body replaces the type, so read it first and edit what you get:\n\nExamples:\n  umbraco content-types get blogPost -o json | jq .data > t.json\n  # ...edit t.json...\n  umbraco content-types update blogPost --json-body t.json\n  umbraco content-types update --schema"
        );
        var idArg = new Argument<string?>("id")
        {
            Description = "Document type alias or UUID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        cmd.Add(idArg);
        var body = RawBodyCommand.AddBodyOptions(cmd);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (string.IsNullOrEmpty(result.GetValue(idArg)) || !body.HasBody(result))
                result.AddError(
                    "Supply the document type and --json-body. "
                        + "Run with --schema to print a real document type as a starting point."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        "content-types.update",
                        (client, c) =>
                            RawBodyCommand.ExampleAsync(
                                client.GetDocumentTypeIdsAsync,
                                client.GetDocumentTypeRawAsync,
                                "document types",
                                c
                            ),
                        ct
                    );

                return executor.RunMessageAsync(
                    parseResult,
                    "content-types.update",
                    async (client, c) =>
                    {
                        // Accept the alias the rest of the noun accepts (#159), so a caller never
                        // has to look an id up just to write back what they just read.
                        var resolved = await client.GetDocumentTypeAsync(
                            parseResult.GetValue(idArg)!,
                            c
                        );
                        if (!resolved.IsSuccess)
                            return UmbracoResponse<Empty>.Failure(
                                resolved.StatusCode,
                                resolved.ErrorMessage!,
                                resolved.Category
                            );

                        return await client.UpdateDocumentTypeRawAsync(
                            resolved.Data!.Id,
                            JsonNode.Parse(await body.ReadAsync(parseResult, c))
                                ?? throw new InvalidOperationException("Invalid JSON body."),
                            c
                        );
                    },
                    "Document type updated.",
                    ct
                );
            }
        );

        return cmd;
    }
}
