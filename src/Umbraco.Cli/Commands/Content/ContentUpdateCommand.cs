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
            "Update an existing content item from a JSON body file.\n\nValues and variants in the body are MERGED into the item, matched on alias + culture + segment: anything you leave out keeps its current value, and the item's template is preserved. Pass --replace for the old behaviour, where the body's values and variants replace the item's wholesale.\n\nExamples:\n  umbraco content update 3f7a8b2e-... --json-body ./update.json\n  umbraco content update 3f7a8b2e-... --json-body ./full.json --replace\n  umbraco content update 3f7a8b2e-... --json-body ./update.json --template blogPost"
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
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) containing the update request body. "
                + "Required unless --schema is used."
        );
        var replaceOpt = new Option<bool>("--replace")
        {
            Description =
                "Replace the item's values and variants with the body's instead of merging into them. "
                + "Anything absent from the body is cleared.",
        };
        var templateOpt = new Option<string?>("--template")
        {
            Description =
                "Template to set on the item, by alias or UUID. Omitted, the current template is kept.",
        };
        cmd.Add(idArg);
        cmd.Add(replaceOpt);
        cmd.Add(templateOpt);
        body.AddTo(cmd);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (result.GetValue(idArg) is null || !body.HasBody(result))
                result.AddError(
                    "Supply the content id and --json-body. "
                        + "Run with --schema to see the JSON body shape."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                // --schema is a local describe-and-exit (like --help).
                if (body.SchemaRequested(parseResult))
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

                        var json = await body.ReadAsync(parseResult, c);
                        var request =
                            JsonSerializer.Deserialize<UpdateContentRequest>(json)
                            ?? throw new InvalidOperationException("Invalid JSON body.");

                        // --template wins over a template in the body, the way an explicit flag
                        // normally beats a file the caller may not have written.
                        if (parseResult.GetValue(templateOpt) is { Length: > 0 } template)
                            request = request with { Template = TemplateReference(template) };

                        return await client.UpdateContentAsync(
                            id,
                            request,
                            parseResult.GetValue(replaceOpt),
                            c
                        );
                    },
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// Reads a <c>--template</c> value as either a UUID or an alias, so callers can use whichever
    /// they have to hand without a second flag.
    /// </summary>
    /// <param name="value">The raw option value.</param>
    /// <returns>A reference carrying the id when it parsed as a GUID, the alias otherwise.</returns>
    internal static ContentTemplateReference TemplateReference(string value) =>
        Guid.TryParse(value, out var id)
            ? new ContentTemplateReference { Id = id }
            : new ContentTemplateReference { Alias = value };
}
