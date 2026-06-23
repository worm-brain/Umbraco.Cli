using System.CommandLine;

namespace Umbraco.Cli.Commands.Users;

public static class UsersCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("users", "Manage Umbraco backoffice users.");
        cmd.Add(UsersListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(UsersGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(UsersInviteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
