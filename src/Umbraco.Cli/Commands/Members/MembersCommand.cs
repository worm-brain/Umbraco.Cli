using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("members", "Manage Umbraco members.");
        cmd.Add(MembersListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MembersDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
