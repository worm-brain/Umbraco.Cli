using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Members;

/// <summary>Wires the <c>member</c> noun: front-end members (not backoffice users).</summary>
public static class MembersCommand
{
    /// <summary>Builds the <c>member</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
