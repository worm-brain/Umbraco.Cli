using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

public static class UsersListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List Umbraco backoffice users.").WithExamples(
            "umbraco user list",
            "umbraco user list --take 20 --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetUsersAsync(skip, take, c),
                    ["ID", "Name", "Email", "State"],
                    i => new[] { i.Id.ToString(), i.Name, i.Email, i.State },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
