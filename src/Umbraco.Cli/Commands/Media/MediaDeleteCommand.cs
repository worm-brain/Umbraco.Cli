using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Permanently delete a media item by UUID.\n\nExample:\n  umbraco media delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.delete",
                    (client, c) => client.DeleteMediaAsync(parseResult.GetValue(idArg), c),
                    "Media item deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete media item {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
