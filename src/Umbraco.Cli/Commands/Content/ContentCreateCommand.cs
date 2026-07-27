using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new content item. Supply --json-body for full property control.\n\nExamples:\n  umbraco content create --content-type textPage --name \"About\"\n  umbraco content create --content-type textPage --name \"Child\" --parent <id>\n  umbraco content create --content-type blogPost --name \"Post\" --json-body ./body.json"
        );
        // Not marked Required at parse level: a create can be driven by --content-type + --name
        // OR by --json-body OR short-circuited by --schema. The conditional requirement is
        // enforced by a parse-level validator below, so a missing input is a proper parse error
        // (with usage help, before any host/auth work) rather than a late runtime failure.
        var typeOpt = new Option<string>("--content-type")
        {
            Description =
                "Alias of the document type to create (e.g. textPage, blogPost). "
                + "Required unless --json-body or --schema is used.",
        };
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Display name for the new content item. "
                + "Required unless --json-body or --schema is used.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent content item UUID. Omit to create at the root.",
        };
        var bodyOpt = new Option<string?>("--json-body")
        {
            Description =
                "Path to a JSON file (or - for stdin) containing the full create request body "
                + "(overrides other flags).",
        };
        var schemaOpt = new Option<bool>("--schema")
        {
            Description =
                "Print the JSON Schema for the --json-body request and exit (no host/auth needed).",
        };
        cmd.Add(typeOpt);
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(bodyOpt);
        cmd.Add(schemaOpt);

        // Parse-level conditional requirement: unless --schema (describe-and-exit) or a
        // --json-body is given, both --content-type and --name are required. Emitting this as a
        // parse error keeps usage help and a fast, local, argument-level failure.
        cmd.Validators.Add(result =>
        {
            if (result.GetValue(schemaOpt) || !string.IsNullOrEmpty(result.GetValue(bodyOpt)))
                return;
            if (
                string.IsNullOrEmpty(result.GetValue(typeOpt))
                || string.IsNullOrEmpty(result.GetValue(nameOpt))
            )
                result.AddError(
                    "Supply --content-type and --name, or --json-body. "
                        + "Run with --schema to see the JSON body shape."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                // --schema is a local describe-and-exit (like --help): print the body schema
                // without touching the API.
                if (parseResult.GetValue(schemaOpt))
                {
                    JsonBodySchema.Print<CreateContentRequest>();
                    return Task.FromResult(0);
                }

                return executor.RunObjectAsync(
                    parseResult,
                    "content.create",
                    async (client, c) =>
                    {
                        CreateContentRequest request;
                        var bodySource = parseResult.GetValue(bodyOpt);
                        if (!string.IsNullOrEmpty(bodySource))
                        {
                            var json = await JsonBodyInput.ReadAsync(bodySource, c);
                            request =
                                JsonSerializer.Deserialize<CreateContentRequest>(json)
                                ?? throw new InvalidOperationException("Invalid JSON body.");
                        }
                        else
                        {
                            // The validator above guarantees both are present in this branch.
                            var alias = parseResult.GetValue(typeOpt)!;
                            var name = parseResult.GetValue(nameOpt)!;
                            var parentId = parseResult.GetValue(parentOpt);
                            request = new CreateContentRequest
                            {
                                ContentType = new ContentTypeReference { Alias = alias },
                                Parent = parentId.HasValue
                                    ? new ContentParentReference { Id = parentId.Value }
                                    : null,
                                Variants = [new ContentVariant { Name = name }],
                            };
                        }

                        return await client.CreateContentAsync(request, c);
                    },
                    ct
                );
            }
        );

        return cmd;
    }
}
