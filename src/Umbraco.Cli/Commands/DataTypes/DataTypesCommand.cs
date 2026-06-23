using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("data-types", "List and inspect Umbraco data types.");
        cmd.Add(DataTypesListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(DataTypesGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
