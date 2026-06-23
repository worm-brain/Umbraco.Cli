using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("members", "Manage Umbraco front-end members (not back-office users).\n\nExamples:\n  umbraco members list\n  umbraco members create --email user@example.com --name \"Jane Doe\" --type Member\n  umbraco members delete <id>");
        cmd.Add(MembersListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
