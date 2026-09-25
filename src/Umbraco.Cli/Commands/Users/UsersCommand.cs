using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "user",
            "Manage Umbraco backoffice users (not front-end members).\n\nExamples:\n  umbraco user list\n  umbraco user invite --email editor@example.com --name \"John Smith\"\n  umbraco user get <id>"
        );
        cmd.Add(UsersListCommand.Build(executor));
        cmd.Add(UsersGetCommand.Build(executor));
        cmd.Add(UsersInviteCommand.Build(executor));
        return cmd;
    }
}
