using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires <c>media-type get</c>.</summary>
public static class MediaTypesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the media type's verbatim Management API body (#250 Phase 5,
    /// #221), which is the shape <c>media-type update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a media type by id or alias: the full Management API body, with its properties, groups and allowed child types. The output is a valid 'update --json-body'.\n\nExample:\n  umbraco media-type get brochure"
        );
        var idArg = Reference.Argument(EntityKind.MediaType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(executor, parseResult, SchemaNoun.MediaTypes, idArg, ct)
        );

        return cmd;
    }
}
