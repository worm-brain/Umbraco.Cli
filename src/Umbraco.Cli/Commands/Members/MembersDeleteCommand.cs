using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Permanently delete a member by UUID.\n\nExample:\n  umbraco members delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "members.delete",
                    (client, c) => client.DeleteMemberAsync(parseResult.GetValue(idArg), c),
                    "Member deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete member {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
