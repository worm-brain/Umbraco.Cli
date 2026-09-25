using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DocumentBlueprints;

/// <summary>
/// Wires the <c>document-blueprint</c> noun (issue #113) - content templates authors start new
/// documents from. Verbs: list/get/create (from flags, a body, or --from-document)/update/delete, move, scaffold
/// preview, and a <c>folder</c> sub-noun. The create/update body mirrors <c>content create</c>
/// (scalar flags + <c>--json-body</c> + <c>--schema</c>); get/scaffold/create emit raw JSON.
/// </summary>
public static class DocumentBlueprintCommand
{
    /// <summary>Builds the <c>document-blueprint</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "document-blueprint",
            "List, inspect, and manage Umbraco document blueprints (content templates).\n\nExamples:\n  umbraco document-blueprint list\n  umbraco document-blueprint create --from-document <id> --name \"Starter\""
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildScaffold(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        cmd.Add(BuildMove(executor));
        cmd.Add(BlueprintFolderCommand.Build(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List blueprints. With --parent, lists the children of that folder; otherwise the root."
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent folder UUID to list children of; omit for the tree root.",
        };
        cmd.Add(parentOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetDocumentBlueprintsAsync(
                            parseResult.GetValue(parentOpt),
                            skip,
                            take,
                            c
                        ),
                    new[] { "Id", "Name", "IsFolder", "HasChildren" },
                    b =>
                        new[]
                        {
                            b.Id.ToString(),
                            b.Name,
                            b.IsFolder.ToString(),
                            b.HasChildren.ToString(),
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a blueprint by UUID as raw JSON (full fidelity).");
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetDocumentBlueprintAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildScaffold(CommandExecutor executor)
    {
        var cmd = new Command(
            "scaffold",
            "Print the pre-filled create template Umbraco would use to start a document from this blueprint."
        );
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.ScaffoldDocumentBlueprintAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a blueprint from flags, a JSON body, or an existing document.\n\nExamples:\n  umbraco document-blueprint create --document-type textPage --name \"Starter\"\n  umbraco document-blueprint create --from-document <id> --name \"Starter\"\n  umbraco document-blueprint create --json-body ./bp.json"
        ).Mutating();
        // Conditional requirement (mirrors content create): --document-type + --name, OR
        // --from-document + --name, OR --json-body, OR --schema (describe-and-exit). Enforced by the
        // validator below as a parse error.
        var typeOpt = new Option<string>("--document-type")
        {
            Description =
                "Document type alias or UUID the blueprint is based on. "
                + "Required unless --json-body or --schema is used.",
        };
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Blueprint display name. Required unless --json-body or --schema is used.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent folder UUID. Omit to create at the blueprint root.",
        };
        // Creating from something else is an option on create, not its own verb (docs/conventions.md 2).
        var fromDocumentOpt = new Option<Guid?>("--from-document")
        {
            Description =
                "Copy an existing document into the new blueprint. Takes --name (applied to "
                + "every culture), --parent and --id; not --document-type, --culture or --json-body.",
        };
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) with the full create body; the field flags go in the body instead."
        );
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description =
                "Culture of the name (e.g. en-US). Omitted, a type that varies by culture gets the "
                + "default language and an invariant type gets none.",
        };
        cmd.Add(typeOpt);
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(fromDocumentOpt);
        body.AddTo(cmd);
        cmd.Add(idOpt);
        cmd.Add(cultureOpt);

        cmd.Validators.Add(result =>
        {
            if (result.GetValue(fromDocumentOpt) is not null)
            {
                // The document supplies the type, values and cultures, so options that would set
                // them conflict rather than being silently ignored.
                if (
                    !string.IsNullOrEmpty(result.GetValue(typeOpt))
                    || body.HasBody(result)
                    || result.GetValue(cultureOpt) is not null
                )
                    result.AddError(
                        "--from-document copies the document's type, values and cultures; "
                            + "drop --document-type, --json-body and --culture."
                    );
                else if (string.IsNullOrEmpty(result.GetValue(nameOpt)))
                    result.AddError("--from-document needs --name for the new blueprint.");
                return;
            }
            if (body.SchemaRequested(result))
                return;
            if (body.HasBody(result))
            {
                // The body is the whole request; a flag beside it would be silently dropped.
                if (
                    !string.IsNullOrEmpty(result.GetValue(typeOpt))
                    || !string.IsNullOrEmpty(result.GetValue(nameOpt))
                    || result.GetValue(parentOpt) is not null
                    || result.GetValue(idOpt) is not null
                    || result.GetValue(cultureOpt) is not null
                )
                    result.AddError(
                        "--json-body is the whole create request; put the document type, name, "
                            + "parent, id and culture in it rather than passing them as flags."
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
                if (body.SchemaRequested(parseResult))
                {
                    JsonBodySchema.Print<CreateDocumentBlueprintRequest>();
                    return Task.FromResult(0);
                }

                if (parseResult.GetValue(fromDocumentOpt) is { } source)
                    return executor.RunObjectAsync(
                        parseResult,
                        (client, c) =>
                            client.CreateDocumentBlueprintFromDocumentAsync(
                                new CreateBlueprintFromDocumentRequest
                                {
                                    Id = parseResult.GetValue(idOpt),
                                    Document = source,
                                    Name = parseResult.GetValue(nameOpt),
                                    Parent = parseResult.GetValue(parentOpt) is { } p
                                        ? new ContentParentReference { Id = p }
                                        : null,
                                },
                                c
                            ),
                        ct
                    );

                return executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        CreateDocumentBlueprintRequest request;
                        if (body.HasBody(parseResult))
                        {
                            var json = await body.ReadAsync(parseResult, c);
                            request =
                                JsonSerializer.Deserialize<CreateDocumentBlueprintRequest>(json)
                                ?? throw new InvalidInputException("Invalid JSON body.");
                        }
                        else
                        {
                            // The validator guarantees both are present in this branch.
                            var typeStr = parseResult.GetValue(typeOpt)!;
                            var name = parseResult.GetValue(nameOpt)!;
                            var parentId = parseResult.GetValue(parentOpt);
                            request = new CreateDocumentBlueprintRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                DocumentType = ParseDocumentType(typeStr),
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

                        return await client.CreateDocumentBlueprintAsync(request, c);
                    },
                    ct
                );
            }
        );
        return cmd;
    }

    private static Command BuildUpdate(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a blueprint's values and variants. They are merged into the blueprint, as "
                + "'content update' does; --replace sends them as the whole set instead.\n\n"
                + "Example:\n  umbraco document-blueprint update <id> --json-body ./bp.json"
        ).Mutating();
        // id is nullable/optional at the PARSE level only so --schema can describe the body without
        // it; the validator below makes it required for an actual update.
        var idArg = new Argument<Guid?>("id")
        {
            Description = "Blueprint ID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var nameOpt = new Option<string>("--name")
        {
            Description = "New display name. Required unless --json-body or --schema is used.",
        };
        var updateCultureOpt = new Option<string?>("--culture")
        {
            Description =
                "Culture of the new name (e.g. en-US). Omitted, a blueprint that varies by "
                + "culture renames its default-language variant.",
        };
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) with the update body; merged into the blueprint unless --replace (overrides --name)."
        );
        // #242: content update merges and blueprint update replaced, so the same body lost fields
        // on one and not the other. Both merge now; --replace is the way to the old behaviour.
        var replaceOpt = new Option<bool>("--replace")
        {
            Description =
                "Replace the blueprint's values and variants with the ones given, instead of merging.",
        };
        // Replacing drops whatever is not given, which the CLI cannot restore (docs/conventions.md 5.2).
        cmd.DestructiveWith(
            replaceOpt,
            _ => "Replace this blueprint's values and variants, clearing anything not given?"
        );
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.Add(updateCultureOpt);
        cmd.Add(replaceOpt);
        body.AddTo(cmd);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (result.GetValue(idArg) is null)
                result.AddError(
                    "Supply the blueprint id. Run with --schema to see the body shape."
                );
            if (!body.HasBody(result) && string.IsNullOrEmpty(result.GetValue(nameOpt)))
                result.AddError(
                    "Supply --name, or --json-body. Run with --schema to see the JSON body shape."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                {
                    JsonBodySchema.Print<UpdateDocumentBlueprintRequest>();
                    return Task.FromResult(0);
                }

                return executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        UpdateDocumentBlueprintRequest request;
                        if (body.HasBody(parseResult))
                        {
                            var json = await body.ReadAsync(parseResult, c);
                            request =
                                JsonSerializer.Deserialize<UpdateDocumentBlueprintRequest>(json)
                                ?? throw new InvalidInputException("Invalid JSON body.");
                        }
                        else
                        {
                            request = new UpdateDocumentBlueprintRequest
                            {
                                Variants =
                                [
                                    new ContentVariant
                                    {
                                        Name = parseResult.GetValue(nameOpt)!,
                                        Culture = parseResult.GetValue(updateCultureOpt),
                                    },
                                ],
                            };
                        }

                        var id = parseResult.GetValue(idArg)!.Value;
                        // An update's data is the resulting item, as get shows it (docs/conventions.md 6.2).
                        return await client
                            .UpdateDocumentBlueprintAsync(
                                id,
                                request,
                                parseResult.GetValue(replaceOpt),
                                c
                            )
                            .ThenRead(() => client.GetDocumentBlueprintAsync(id, c));
                    },
                    "Blueprint updated.",
                    ct
                );
            }
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a blueprint by UUID.").Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete blueprint {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteDocumentBlueprintAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Blueprint deleted.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildMove(CommandExecutor executor)
    {
        var cmd = new Command(
            "move",
            "Move a blueprint under a folder (or to the root when --parent is omitted)."
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        var targetOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Destination folder UUID; omit to move to the root. --target works too.",
        };
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .MoveDocumentBlueprintAsync(
                                parseResult.GetValue(idArg),
                                parseResult.GetValue(targetOpt),
                                c
                            )
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Blueprint moved.",
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// Parses a <c>--document-type</c> value that may be either a UUID or an alias into a
    /// <see cref="ContentTypeReference"/> (the client resolves an alias to its id).
    /// </summary>
    /// <param name="value">The raw option value.</param>
    /// <returns>A reference carrying the id (if a UUID) or the alias.</returns>
    private static ContentTypeReference ParseDocumentType(string value) =>
        Guid.TryParse(value, out var id)
            ? new ContentTypeReference { Id = id }
            : new ContentTypeReference { Alias = value };
}
