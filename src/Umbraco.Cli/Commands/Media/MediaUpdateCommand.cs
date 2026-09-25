using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires <c>media update</c> (#220).</summary>
public static class MediaUpdateCommand
{
    /// <summary>
    /// Builds the command. Values and names are merged into the item, as <c>content update</c>
    /// does, so the uploaded file and every value not named keep what they have.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a media item's property values and name. They are MERGED into the item, matched on alias + culture + segment: anything you leave out, the uploaded file included, keeps its current value. --replace sends the body's values and variants as the whole set.\n\nExamples:\n  umbraco media update 3f7a8b2e-... --value summary=\"Forms for new members\" --value pageCount=4\n  umbraco media update 3f7a8b2e-... --name \"Membership forms\"\n  umbraco media update 3f7a8b2e-... --json-body ./media.json"
        ).Mutating();
        // Optional at parse level only so --schema can describe the body without it.
        var idArg = new Argument<Guid?>("id")
        {
            Description = "Media item ID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var nameOpt = new Option<string?>("--name") { Description = "New display name." };
        var valueOpt = new Option<string[]>("--value")
        {
            Description = "Property values as alias=value, e.g. summary=\"...\". Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };
        var replaceOpt = new Option<bool>("--replace")
        {
            Description =
                "Replace the item's values and variants with the body's instead of merging into them. "
                + "Anything absent from the body is cleared - the uploaded file included.",
        };
        // Replacing drops whatever is not given, which the CLI cannot restore (docs/conventions.md 5.2).
        cmd.DestructiveWith(
            replaceOpt,
            _ =>
                "Replace this media item's values and variants, clearing anything the body leaves out (the file included)?"
        );
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) with values and variants to merge; --name and "
                + "--value are applied on top of it."
        );
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.Add(valueOpt);
        cmd.Add(replaceOpt);
        body.AddTo(cmd);
        KeyValuePairs.Validate(cmd, valueOpt, "--value must be alias=value, e.g. title=Brochure");

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            var values = KeyValuePairs.Parse(result.GetValue(valueOpt)).ToList();
            var hasChange =
                body.HasBody(result)
                || !string.IsNullOrEmpty(result.GetValue(nameOpt))
                || values.Count > 0;
            if (result.GetValue(idArg) is null || !hasChange)
                result.AddError(
                    "Supply the media id and at least one of --name, --value or --json-body. "
                        + "Run with --schema to see the JSON body shape."
                );
            else if (result.GetValue(replaceOpt) && !body.HasBody(result))
                result.AddError(
                    "--replace needs --json-body: with no body it would clear every value."
                );
            // The file is umbracoFile, which only an upload can set; a flag value would break it.
            if (
                values.Any(p =>
                    string.Equals(p.Key, "umbracoFile", StringComparison.OrdinalIgnoreCase)
                )
            )
                result.AddError("--value cannot set umbracoFile: upload a new file instead.");
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                {
                    JsonBodySchema.Print<UpdateMediaRequest>();
                    return Task.FromResult(0);
                }

                return executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var request = body.HasBody(parseResult)
                            ? JsonSerializer.Deserialize<UpdateMediaRequest>(
                                await body.ReadAsync(parseResult, c)
                            ) ?? throw new InvalidInputException("Invalid JSON body.")
                            : new UpdateMediaRequest();

                        // Flags win over the body, the way an explicit flag beats a file, and are
                        // laid on it with the merge's own key (alias + culture + segment), so a
                        // flag replaces only the invariant entry it names.
                        request = request with
                        {
                            Values = DocumentUpdateBody.Overlay(
                                request.Values,
                                KeyValuePairs
                                    .Parse(parseResult.GetValue(valueOpt))
                                    .Select(p => new ContentValue
                                    {
                                        Alias = p.Key,
                                        Value = p.Value,
                                    })
                            ),
                            Variants = parseResult.GetValue(nameOpt) is { Length: > 0 } name
                                ? DocumentUpdateBody.Overlay(
                                    request.Variants,
                                    [new ContentVariant { Name = name }]
                                )
                                : request.Variants,
                        };

                        return await client.UpdateMediaAsync(
                            parseResult.GetValue(idArg)!.Value,
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
}
