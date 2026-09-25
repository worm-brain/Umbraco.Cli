using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires <c>media-type update</c> (#221).</summary>
public static class MediaTypesUpdateCommand
{
    /// <summary>
    /// Builds the command. There was no update at all, so adding a field to a media type or
    /// allowing it inside a folder meant a whole-instance <c>schema export -&gt; apply</c>. This
    /// takes the shape <c>media-type get</c> prints and merges it into the type.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a media type from a Management API body - its properties, groups, allowed child types and icon. The body's top-level keys are merged into the type, so a key you leave out keeps its value; --replace sends the body as the whole type.\n\nExamples:\n  umbraco media-type get brochure -o json | jq .data > mt.json\n  # ...edit mt.json...\n  umbraco media-type update brochure --json-body mt.json\n  umbraco media-type update --schema"
        ).Mutating();
        var options = RawBodyCommand.AddUpdateOptions(cmd, SchemaNoun.MediaTypes, hasFlags: false);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunUpdateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.MediaTypes,
                    options,
                    "Media type updated.",
                    ct
                )
        );

        return cmd;
    }
}
