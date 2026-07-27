using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUpdateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update an existing content item by replacing its values with those from a JSON body file.\n\nExample:\n  umbraco content update 3f7a8b2e-... --json-body ./update.json"
        );
        // id and --json-body are only optional at the PARSE level so that `--schema` can
        // describe the body without them; both are required for an actual update and validated
        // in the action.
        var idArg = new Argument<Guid>("id")
        {
            Description = "Content item ID.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var bodyOpt = new Option<FileInfo?>("--json-body")
        {
            Description = "Path to a JSON file containing the update request body.",
        };
        var schemaOpt = new Option<bool>("--schema")
        {
            Description =
                "Print the JSON Schema for the --json-body request and exit (no host/auth needed).",
        };
        cmd.Add(idArg);
        cmd.Add(bodyOpt);
        cmd.Add(schemaOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                // --schema is a local describe-and-exit (like --help).
                if (parseResult.GetValue(schemaOpt))
                {
                    JsonBodySchema.Print<UpdateContentRequest>();
                    return Task.FromResult(0);
                }

                return executor.RunObjectAsync(
                    parseResult,
                    "content.update",
                    async (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        var bodyFile = parseResult.GetValue(bodyOpt);
                        if (id == Guid.Empty || bodyFile is null)
                            throw new InvalidOperationException(
                                "Supply the content id and --json-body. Run with --schema to "
                                    + "see the JSON body shape."
                            );

                        var json = await File.ReadAllTextAsync(bodyFile.FullName, c);
                        var request =
                            JsonSerializer.Deserialize<UpdateContentRequest>(json)
                            ?? throw new InvalidOperationException("Invalid JSON body.");

                        return await client.UpdateContentAsync(id, request, c);
                    },
                    ct
                );
            }
        );

        return cmd;
    }
}
