using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "member",
            "Manage Umbraco front-end members (not backoffice users).\n\nExamples:\n  umbraco member list\n  umbraco member create --email user@example.com --name \"Jane Doe\" --member-type Member\n  umbraco member delete <id>"
        );
        cmd.Add(MembersListCommand.Build(executor));
        cmd.Add(MembersGetCommand.Build(executor));
        cmd.Add(MembersCreateCommand.Build(executor));
        cmd.Add(MembersUpdateCommand.Build(executor));
        cmd.Add(MembersDeleteCommand.Build(executor));
        return cmd;
    }
}
