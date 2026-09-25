using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

public static class MediaDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a media item permanently, by id.\n\nExample:\n  umbraco media delete 3f7a8b2e-..."
        ).Mutating();
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.Destructive(parseResult =>
            $"Permanently delete media item {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.DeleteMediaAsync(parseResult.GetValue(idArg), c),
                    "Media item deleted.",
                    ct
                )
        );

        return cmd;
    }
}
