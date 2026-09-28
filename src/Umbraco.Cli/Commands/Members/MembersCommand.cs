using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "member",
            "Manage Umbraco front-end members (not backoffice users)."
        ).WithExamples(
            "umbraco member list",
            "umbraco member create --email user@example.com --name \"Jane Doe\" --member-type Member",
            "umbraco member delete <id>"
        );
        cmd.Add(MembersListCommand.Build(executor));
        cmd.Add(MembersGetCommand.Build(executor));
        cmd.Add(MembersCreateCommand.Build(executor));
        cmd.Add(MembersUpdateCommand.Build(executor));
        cmd.Add(MembersDeleteCommand.Build(executor));
        return cmd;
    }
}
