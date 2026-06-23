using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("dictionary", "Manage Umbraco dictionary items.");
        cmd.Add(DictionaryListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(DictionaryGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(DictionaryCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
