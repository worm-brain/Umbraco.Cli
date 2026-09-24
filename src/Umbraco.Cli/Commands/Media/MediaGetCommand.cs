using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a media item by its UUID.\n\nReturns id, name, mediaType, createDate and updateDate. The file URL, dimensions, size and extension are NOT returned yet - read them from the Management API ('GET /umbraco/management/api/v1/media/{id}' for values, or '/media/urls?id=') until then.\n\nExample:\n  umbraco media get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "media.get",
                    (client, c) => client.GetMediaByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
