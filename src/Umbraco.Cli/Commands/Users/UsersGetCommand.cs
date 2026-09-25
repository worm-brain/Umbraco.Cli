using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a back-office user by their UUID.\n\nExample:\n  umbraco users get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetUserByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
