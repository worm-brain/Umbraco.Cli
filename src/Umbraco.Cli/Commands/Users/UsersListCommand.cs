using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List Umbraco back-office users.\n\nExample:\n  umbraco users list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "users.list",
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
