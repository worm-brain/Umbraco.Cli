using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-types get</c> command (issue #55).</summary>
public static class MediaTypesGetCommand
{
    /// <summary>Builds the <c>media-types get</c> command (fetch a media type by id).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a media type by id or alias, including its alias and description.\n\nExample:\n  umbraco media-types get brochure"
        );
        var idArg = Reference.Argument(EntityKind.MediaType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "media-types.get",
                    (client, c) =>
                        client.WithResolvedAsync(
                            EntityKind.MediaType,
                            parseResult.GetValue(idArg)!,
                            id => client.GetMediaTypeByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
