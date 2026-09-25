using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a member by their UUID.\n\nExample:\n  umbraco member get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetMemberByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
