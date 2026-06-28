using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("users", "Manage Umbraco back-office users (not front-end members).\n\nExamples:\n  umbraco users list\n  umbraco users invite --email editor@example.com --name \"John Smith\"\n  umbraco users get <id>");
        cmd.Add(UsersListCommand.Build(executor));
        cmd.Add(UsersGetCommand.Build(executor));
        cmd.Add(UsersInviteCommand.Build(executor));
        return cmd;
    }
}
