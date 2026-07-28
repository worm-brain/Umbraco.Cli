using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "members",
            "Manage Umbraco front-end members (not back-office users).\n\nExamples:\n  umbraco members list\n  umbraco members create --email user@example.com --name \"Jane Doe\" --type Member\n  umbraco members delete <id>"
        );
        cmd.Add(MembersListCommand.Build(executor));
        cmd.Add(MembersGetCommand.Build(executor));
        cmd.Add(MembersCreateCommand.Build(executor));
        cmd.Add(MembersUpdateCommand.Build(executor));
        cmd.Add(MembersDeleteCommand.Build(executor));
        return cmd;
    }
}
