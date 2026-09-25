using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Members;

public static class MembersDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a member permanently, by id.\n\nExample:\n  umbraco member delete 3f7a8b2e-..."
        ).Mutating();
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete member {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.DeleteMemberAsync(parseResult.GetValue(idArg), c),
                    "Member deleted.",
                    ct
                )
        );

        return cmd;
    }
}
