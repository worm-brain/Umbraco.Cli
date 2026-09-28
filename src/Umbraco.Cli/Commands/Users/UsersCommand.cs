using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

/// <summary>Wires the <c>user</c> noun: backoffice users (not front-end members).</summary>
public static class UsersCommand
{
    /// <summary>Builds the <c>user</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "user",
            "Manage Umbraco backoffice users (not front-end members).\n\n"
                + "Umbraco hides the super-user (the installer's administrator) from every other user, "
                + "so it is missing from 'user list' and cannot be named unless you are signed in as it."
        ).WithExamples(
            "umbraco user list",
            "umbraco user create --email editor@example.com --name \"John Smith\" --group editor --password <secret>",
            "umbraco user get editor@example.com"
        );
        cmd.Add(UsersListCommand.Build(executor));
        cmd.Add(UsersGetCommand.Build(executor));
        cmd.Add(UsersCreateCommand.Build(executor));
        cmd.Add(UsersUpdateCommand.Build(executor));
        cmd.Add(UsersDeleteCommand.Build(executor));
        cmd.Add(UsersInviteCommand.Build(executor));
        return cmd;
    }
}
