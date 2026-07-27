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
        // OR by --json-body OR short-circuited by --schema, so the requirement is conditional
        // and validated in the action below (this also makes the #60 catalog's "required"
        // honest — these flags are genuinely optional when --json-body is used).
        var typeOpt = new Option<string>("--content-type")
        {
            Description = "Alias of the document type to create (e.g. textPage, blogPost).",
        };
        var nameOpt = new Option<string>("--name")
        {
            Description = "Display name for the new content item.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent content item UUID. Omit to create at the root.",
        };
        var bodyOpt = new Option<FileInfo?>("--json-body")
        {
            Description =
                "Path to a JSON file containing the full create request body (overrides other flags).",
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
                        var bodyFile = parseResult.GetValue(bodyOpt);
                        if (bodyFile is not null)
                        {
                            var json = await File.ReadAllTextAsync(bodyFile.FullName, c);
                            request =
                                JsonSerializer.Deserialize<CreateContentRequest>(json)
                                ?? throw new InvalidOperationException("Invalid JSON body.");
                        }
                        else
                        {
                            var alias = parseResult.GetValue(typeOpt);
                            var name = parseResult.GetValue(nameOpt);
                            // Conditional requirement (see the option definitions): without a
                            // --json-body, both --content-type and --name are needed.
                            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(name))
                                throw new InvalidOperationException(
                                    "Supply --content-type and --name, or --json-body. "
                                        + "Run with --schema to see the JSON body shape."
                                );

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
