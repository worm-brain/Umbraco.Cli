using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

public static class MediaGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a media item by id, including its public URL and file metadata.\n\nWidth, height, size and extension come back under 'values', keyed by the aliases Umbraco uses (umbracoWidth, umbracoHeight, umbracoBytes, umbracoExtension); the URL comes back under 'urls', one entry per culture."
        ).WithExamples(
            "umbraco media get 3f7a8b2e-...",
            "umbraco media get <id> -o json | jq .data.urls"
        );
        var idArg = new Argument<Guid>("id") { Description = "Media item id." };
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
