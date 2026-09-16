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
                executor.RunTableAsync(
                    parseResult,
                    "users.list",
                    (client, c) =>
                        client.GetUsersAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Email", "State"],
                    data =>
                        data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Email, i.State })
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
