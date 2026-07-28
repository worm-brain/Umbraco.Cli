using System.CommandLine;

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
            "Get a media type by UUID, including its alias and description.\n\nExample:\n  umbraco media-types get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "media-types.get",
                    (client, c) => client.GetMediaTypeByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
