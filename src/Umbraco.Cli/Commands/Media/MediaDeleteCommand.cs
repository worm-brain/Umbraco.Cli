using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

public static class MediaDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a media item permanently, by id.")
            .WithExamples("umbraco media delete 3f7a8b2e-...", "umbraco media delete <id> --yes")
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Media item id." };
        cmd.Add(idArg);

        cmd.Destructive(parseResult =>
            $"Permanently delete media item {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteMediaAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Media item deleted.",
                    ct
                )
        );

        return cmd;
    }
}
