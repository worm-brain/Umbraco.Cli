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
        // id and --json-body are optional at the PARSE level only so that `--schema` can
        // describe the body without them. A nullable id makes "omitted" (null) unambiguous
        // versus an explicit all-zero GUID. Both are required for an actual update, enforced by
        // the parse-level validator below (a proper, local parse error).
        var idArg = new Argument<Guid?>("id")
        {
            Description = "Content item ID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var bodyOpt = new Option<string?>("--json-body")
        {
            Description =
                "Path to a JSON file (or - for stdin) containing the update request body. "
                + "Required unless --schema is used.",
        };
        var schemaOpt = new Option<bool>("--schema")
        {
            Description =
                "Print the JSON Schema for the --json-body request and exit (no host/auth needed).",
        };
        cmd.Add(idArg);
        cmd.Add(bodyOpt);
        cmd.Add(schemaOpt);

        cmd.Validators.Add(result =>
        {
            if (result.GetValue(schemaOpt))
                return;
            if (result.GetValue(idArg) is null || string.IsNullOrEmpty(result.GetValue(bodyOpt)))
                result.AddError(
                    "Supply the content id and --json-body. "
                        + "Run with --schema to see the JSON body shape."
                );
        });

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
                        // The validator guarantees both are present here.
                        var id = parseResult.GetValue(idArg)!.Value;
                        var bodySource = parseResult.GetValue(bodyOpt)!;

                        var json = await JsonBodyInput.ReadAsync(bodySource, c);
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
