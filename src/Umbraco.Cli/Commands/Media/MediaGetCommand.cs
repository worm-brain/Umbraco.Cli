using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a media item by its UUID, including URL and metadata.\n\nExample:\n  umbraco media get 3f7a8b2e-...");
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "media.get",
            (client, c) => client.GetMediaByIdAsync(parseResult.GetValue(idArg), c),
            ct));

        return cmd;
    }
}
