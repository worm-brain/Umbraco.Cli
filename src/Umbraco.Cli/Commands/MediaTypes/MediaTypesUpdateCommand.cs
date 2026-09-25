using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires <c>media-types update</c> (#221).</summary>
public static class MediaTypesUpdateCommand
{
    /// <summary>
    /// Builds the command. There was no update at all, so adding a field to a media type or
    /// allowing it inside a folder meant a whole-instance <c>schema export -&gt; apply</c>. This
    /// takes the shape <c>media-types get</c> prints and merges it into the type.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a media type from a Management API body - its properties, groups, allowed child types and icon. The body's top-level keys are merged into the type, so a key you leave out keeps its value; --replace sends the body as the whole type.\n\nExamples:\n  umbraco media-types get brochure -o json | jq .data > mt.json\n  # ...edit mt.json...\n  umbraco media-types update brochure --json-body mt.json\n  umbraco media-types update --schema"
        );
        var idArg = new Argument<string?>("id")
        {
            Description = "Media type alias or UUID. Required unless --schema is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        cmd.Add(idArg);
        var body = RawBodyCommand.AddBodyOptions(cmd);
        var replace = RawBodyCommand.AddReplaceOption(cmd);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result))
                return;
            if (string.IsNullOrEmpty(result.GetValue(idArg)) || !body.HasBody(result))
                result.AddError(
                    "Supply the media type and --json-body. "
                        + "Run with --schema to print a real media type as a starting point."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
                body.SchemaRequested(parseResult)
                    ? RawBodyCommand.RunSchemaAsync(
                        executor,
                        parseResult,
                        "media-types.update",
                        client => client.GetMediaTypeIdsAsync,
                        client => client.GetMediaTypeRawAsync,
                        "media types",
                        ct
                    )
                    : RawBodyCommand.RunMergeAsync(
                        executor,
                        parseResult,
                        "media-types.update",
                        EntityKind.MediaType,
                        parseResult.GetValue(idArg)!,
                        body,
                        replace,
                        "Media type updated.",
                        ct
                    )
        );

        return cmd;
    }
}
