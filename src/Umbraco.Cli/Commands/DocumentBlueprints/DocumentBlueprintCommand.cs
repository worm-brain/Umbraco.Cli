using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DocumentBlueprints;

/// <summary>
/// Wires the <c>document-blueprint</c> noun (issue #113) - content templates authors start new
/// documents from. Verbs: list/get/create/update/delete, from-document scaffolding, move, scaffold
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
            "List, inspect, and manage Umbraco document blueprints (content templates).\n\nExamples:\n  umbraco document-blueprint list\n  umbraco document-blueprint from-document <documentId> --name \"Starter\""
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildScaffold(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        cmd.Add(BuildFromDocument(executor));
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
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "document-blueprint.list",
                    (client, c) =>
                        client.GetDocumentBlueprintsAsync(
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Id", "Name", "IsFolder", "HasChildren" },
                    data =>
                        (data?.Items ?? []).Select(b =>
                            new[]
                            {
                                b.Id.ToString(),
                                b.Name,
                                b.IsFolder.ToString(),
                                b.HasChildren.ToString(),
                            }
                        ),
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
                    "document-blueprint.get",
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
                    "document-blueprint.scaffold",
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
            "Create a blueprint. Supply --json-body for full property control.\n\nExamples:\n  umbraco document-blueprint create --document-type textPage --name \"Starter\"\n  umbraco document-blueprint create --json-body ./bp.json"
        );
        // Conditional requirement (mirrors content create): --document-type + --name, OR --json-body,
        // OR --schema (describe-and-exit). Enforced by the validator below as a parse error.
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
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) with the full create body (overrides flags)."
        );
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(typeOpt);
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        body.AddTo(cmd);
        cmd.Add(idOpt);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result) || body.HasBody(result))
                return;
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

                return executor.RunObjectAsync(
                    parseResult,
                    "document-blueprint.create",
                    async (client, c) =>
                    {
                        CreateDocumentBlueprintRequest request;
                        if (body.HasBody(parseResult))
                        {
                            var json = await body.ReadAsync(parseResult, c);
                            request =
                                JsonSerializer.Deserialize<CreateDocumentBlueprintRequest>(json)
                                ?? throw new InvalidOperationException("Invalid JSON body.");
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
                                Variants = [new ContentVariant { Name = name }],
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
            "Update a blueprint's values and variants. Supply --json-body for full control.\n\nExample:\n  umbraco document-blueprint update <id> --json-body ./bp.json"
        );
        // id is nullable/optional at the PARSE level only so --schema can describe the body without
        // it; the validator below makes it required for an actual update.
        var idArg = new Argument<Guid?>("id")
        {
            Description = "Blueprint ID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "New display name (sets a single invariant variant). "
                + "Required unless --json-body or --schema is used.",
        };
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) with the full update body (overrides --name)."
        );
        cmd.Add(idArg);
        cmd.Add(nameOpt);
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
                    "document-blueprint.update",
                    async (client, c) =>
                    {
                        UpdateDocumentBlueprintRequest request;
                        if (body.HasBody(parseResult))
                        {
                            var json = await body.ReadAsync(parseResult, c);
                            request =
                                JsonSerializer.Deserialize<UpdateDocumentBlueprintRequest>(json)
                                ?? throw new InvalidOperationException("Invalid JSON body.");
                        }
                        else
                        {
                            request = new UpdateDocumentBlueprintRequest
                            {
                                Variants =
                                [
                                    new ContentVariant { Name = parseResult.GetValue(nameOpt)! },
                                ],
                            };
                        }

                        return await client.UpdateDocumentBlueprintAsync(
                            parseResult.GetValue(idArg)!.Value,
                            request,
                            c
                        );
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
        var cmd = new Command("delete", "Delete a blueprint by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "document-blueprint.delete",
                    (client, c) =>
                        client.DeleteDocumentBlueprintAsync(parseResult.GetValue(idArg), c),
                    "Blueprint deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete blueprint {parseResult.GetValue(idArg)}?"
                )
        );
        return cmd;
    }

    private static Command BuildFromDocument(CommandExecutor executor)
    {
        var cmd = new Command(
            "from-document",
            "Create a blueprint from an existing document.\n\nExample:\n  umbraco document-blueprint from-document <documentId> --name \"Starter\""
        );
        var docArg = new Argument<Guid>("documentId") { Description = "Source document ID." };
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Name for the new blueprint.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent folder UUID. Omit for the blueprint root.",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(docArg);
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "document-blueprint.from-document",
                    (client, c) =>
                        client.CreateDocumentBlueprintFromDocumentAsync(
                            new CreateBlueprintFromDocumentRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Document = parseResult.GetValue(docArg),
                                Name = parseResult.GetValue(nameOpt),
                                Parent = parseResult.GetValue(parentOpt) is { } p
                                    ? new ContentParentReference { Id = p }
                                    : null,
                            },
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildMove(CommandExecutor executor)
    {
        var cmd = new Command(
            "move",
            "Move a blueprint under a folder (or to the root when --target is omitted)."
        );
        var idArg = new Argument<Guid>("id") { Description = "Blueprint ID." };
        var targetOpt = new Option<Guid?>("--target")
        {
            Description = "Destination folder UUID; omit to move to the root.",
        };
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "document-blueprint.move",
                    (client, c) =>
                        client.MoveDocumentBlueprintAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(targetOpt),
                            c
                        ),
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
