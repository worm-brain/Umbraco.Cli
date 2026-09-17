using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "dictionary",
            "Manage Umbraco dictionary items (translations for static text).\n\nExamples:\n  umbraco dictionary list\n  umbraco dictionary create --key \"Common.Search\" --values en=Search --values da=Søg\n  umbraco dictionary get Common.Search"
        );
        cmd.Add(DictionaryListCommand.Build(executor));
        cmd.Add(DictionaryTreeCommand.Build(executor));
        cmd.Add(DictionaryGetCommand.Build(executor));
        cmd.Add(DictionaryCreateCommand.Build(executor));
        cmd.Add(DictionaryMoveCommand.Build(executor));
        cmd.Add(DictionaryDeleteCommand.Build(executor));
        return cmd;
    }
}
