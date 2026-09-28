using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCreateCommand
{
    /// <summary>
    /// Reads a <c>--json-body</c> into a create request. Besides the CLI's own shape it takes the
    /// Management API's, which is what <c>document-blueprint scaffold</c> prints (#241): a
    /// <c>documentType</c> is read as the <c>contentType</c>. In either shape a body's <c>id</c> is
    /// kept (#299), which is how an idempotent create is scripted (#140) and how exported ids
    /// survive; <c>scaffold</c> no longer prints the blueprint's id, so a piped scaffold still
    /// makes a new item.
    /// </summary>
    /// <param name="json">The body text.</param>
    /// <param name="id">The <c>--id</c> value, or null.</param>
    /// <returns>The request.</returns>
    /// <exception cref="InvalidInputException">
    /// The body is not a JSON object, names no document type, or its id contradicts <c>--id</c>.
    /// </exception>
    internal static CreateContentRequest ReadCreateRequest(string json, Guid? id)
    {
        if (JsonNode.Parse(json) is not JsonObject obj)
            throw new InvalidInputException("--json-body did not contain a JSON object.");

        // The Management API (and scaffold) shape names the type documentType; the CLI's names it
        // contentType. Only a body without the CLI key is translated, so nothing is overridden.
        if (!obj.ContainsKey("contentType") && obj["documentType"] is { } documentType)
        {
            obj.Remove("documentType");
            obj["contentType"] = documentType.DeepClone();
        }

        if (obj["contentType"] is null)
            throw new InvalidInputException(
                "--json-body names no document type: set \"contentType\": { \"alias\": \"...\" }, "
                    + "or pipe in 'document-blueprint scaffold', whose documentType is read as it."
            );

        var request =
            obj.Deserialize<CreateContentRequest>()
            ?? throw new InvalidInputException("Invalid JSON body.");

        return request with
        {
            Id = RawBodyCommand.ReconcileId(id, request.Id),
        };
    }

    /// <summary>Builds the <c>content create</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new content item. Supply --json-body for full property control.\n\nExamples:\n  umbraco content create --document-type textPage --name \"About\"\n  umbraco content create --document-type textPage --name \"Child\" --parent <id>\n  umbraco content create --document-type blogPost --name \"Post\" --culture da-DK\n  umbraco content create --json-body ./body.json\n  umbraco content create --example --document-type blogPost -o json | jq .data > body.json"
        ).Mutating();
        // Not marked Required at parse level: a create can be driven by --document-type + --name
        // OR by --json-body OR short-circuited by --schema. The conditional requirement is
        // enforced by a parse-level validator below, so a missing input is a proper parse error
        // (with usage help, before any host/auth work) rather than a late runtime failure.
        var typeOpt = new Option<string>("--document-type")
        {
            Description =
                "Alias of the document type to create (e.g. textPage, blogPost). "
                + "Required unless --json-body or --schema is used.",
        }.RequiredUnless("--json-body", "--schema");
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Display name for the new content item. "
                + "Required unless --json-body, --schema or --example is used.",
        }.RequiredUnless("--json-body", "--schema", "--example");
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent content item id. Omit to create at the root.",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
        };
        // #228: without a culture a flags-only create on a variant type was rejected with
        // "variance did not match". The client fills in the default language when this is unset.
        var cultureOpt = new Option<string?>("--culture")
        {
            Description =
                "Culture of the name (e.g. en-US). Omitted, a type that varies by culture gets the "
                + "default language and an invariant type gets none.",
        };
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) containing the full create request body. "
                + "--id and --template still apply; the other field flags go in the body instead.",
            // #174: a schema cannot say what each property's value looks like, because that is
            // decided by the data type behind it, so --example reads the document type instead.
            "Print an example --json-body for --document-type, with one values[] entry per "
                + "property filled in for its editor, and exit. --name and --culture fill the "
                + "variant. Requires a host."
        );
        var templateOpt = new Option<string?>("--template")
        {
            Description =
                "Template for the new item, by alias or id. Omitted, the document type's default is used.",
        };
        cmd.Add(typeOpt);
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(idOpt);
        cmd.Add(cultureOpt);
        cmd.Add(templateOpt);
        body.AddTo(cmd);

        // Parse-level conditional requirement: unless --schema (describe-and-exit) or a
        // --json-body is given, both --document-type and --name are required. Emitting this as a
        // parse error keeps usage help and a fast, local, argument-level failure.
        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (body.ExampleRequested(result))
            {
                // The example is built from the document type; --name and --culture fill it in,
                // and anything else would be silently ignored, so it is refused (conventions 4.5).
                if (string.IsNullOrEmpty(result.GetValue(typeOpt)))
                    result.AddError(
                        "--example needs --document-type: the type to build a body for."
                    );
                else if (
                    body.HasBody(result)
                    || result.GetValue(parentOpt) is not null
                    || result.GetValue(idOpt) is not null
                    || result.GetValue(templateOpt) is not null
                )
                    result.AddError(
                        "--example prints a body and exits; it takes --document-type, --name and "
                            + "--culture only, not --json-body, --parent, --id or --template."
                    );
                return;
            }
            if (body.HasBody(result))
            {
                // The body carries these; a flag beside it would be silently dropped
                // (docs/conventions.md 4.5), so it is refused instead.
                if (
                    !string.IsNullOrEmpty(result.GetValue(typeOpt))
                    || !string.IsNullOrEmpty(result.GetValue(nameOpt))
                    || result.GetValue(parentOpt) is not null
                    || result.GetValue(cultureOpt) is not null
                )
                    result.AddError(
                        "--json-body carries the document type, name, parent and culture; put them "
                            + "in the body rather than passing --document-type, --name, --parent or --culture."
                    );
                return;
            }
            if (
                string.IsNullOrEmpty(result.GetValue(typeOpt))
                || string.IsNullOrEmpty(result.GetValue(nameOpt))
            )
                result.AddError(
                    "Supply --document-type and --name, or --json-body. "
                        + "Run with --schema to see the JSON body shape."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                // --schema is a local describe-and-exit (like --help): print the body schema
                // without touching the API.
                if (body.SchemaRequested(parseResult))
                {
                    JsonBodySchema.Print<CreateContentRequest>();
                    return Task.FromResult(0);
                }

                // --example reads the document type and its data types, so unlike --schema it
                // needs a host; the validator guarantees --document-type is set here.
                if (body.ExampleRequested(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        (client, c) =>
                            ContentExampleBody.BuildAsync(
                                client,
                                parseResult.GetValue(typeOpt)!,
                                parseResult.GetValue(cultureOpt),
                                parseResult.GetValue(nameOpt),
                                c
                            ),
                        ct
                    );

                return executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        CreateContentRequest request;
                        if (body.HasBody(parseResult))
                        {
                            request = ReadCreateRequest(
                                await body.ReadAsync(parseResult, c),
                                parseResult.GetValue(idOpt)
                            );
                        }
                        else
                        {
                            // The validator above guarantees both are present in this branch.
                            var alias = parseResult.GetValue(typeOpt)!;
                            var name = parseResult.GetValue(nameOpt)!;
                            var parentId = parseResult.GetValue(parentOpt);
                            request = new CreateContentRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                ContentType = new ContentTypeReference { Alias = alias },
                                Parent = parentId.HasValue
                                    ? new ContentParentReference { Id = parentId.Value }
                                    : null,
                                Variants =
                                [
                                    new ContentVariant
                                    {
                                        Name = name,
                                        Culture = parseResult.GetValue(cultureOpt),
                                    },
                                ],
                            };
                        }

                        // --template wins over one in the body. Left unset the request carries a
                        // null template, which Umbraco reads as "the document type's default".
                        if (parseResult.GetValue(templateOpt) is { Length: > 0 } template)
                            request = request with
                            {
                                Template = ContentUpdateCommand.TemplateReference(template),
                            };

                        return await client.CreateContentAsync(request, c);
                    },
                    ct
                );
            }
        );

        return cmd;
    }
}
