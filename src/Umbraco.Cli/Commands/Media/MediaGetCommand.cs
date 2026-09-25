using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a media item by its UUID, including its public URL and file metadata.\n\nWidth, height, size and extension come back under 'values', keyed by the aliases Umbraco uses (umbracoWidth, umbracoHeight, umbracoBytes, umbracoExtension); the URL comes back under 'urls', one entry per culture.\n\nExample:\n  umbraco media get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetMediaByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
