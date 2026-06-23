using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("dictionary", "Manage Umbraco dictionary items (translations for static text).\n\nExamples:\n  umbraco dictionary list\n  umbraco dictionary create --key \"Common.Search\" --values en=Search --values da=Søg\n  umbraco dictionary get Common.Search");
        cmd.Add(DictionaryListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(DictionaryGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(DictionaryCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
