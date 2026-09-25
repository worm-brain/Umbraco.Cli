using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Permanently delete a content item by ID. This cannot be undone.\n\nExample:\n  umbraco content delete 3f7a8b2e-1234-5678-abcd-ef0123456789"
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);

        cmd.Destructive(parseResult =>
            $"Permanently delete content item {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.DeleteContentAsync(parseResult.GetValue(idArg), c),
                    "Content item deleted.",
                    ct
                )
        );

        return cmd;
    }
}
