using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a backoffice user by id.\n\nExamples:\n  umbraco user get 3f7a8b2e-...\n  umbraco user get <id> -o json | jq .data.email"
        );
        var idArg = new Argument<Guid>("id") { Description = "User id." };
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
