using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "dictionary",
            "Manage Umbraco dictionary items (translations for static text)."
        ).WithExamples(
            "umbraco dictionary list",
            "umbraco dictionary create --key \"Common.Search\" --value en-US=Search --value da-DK=Søg",
            "umbraco dictionary get Common.Search"
        );
        cmd.Add(DictionaryListCommand.Build(executor));
        cmd.Add(DictionaryTreeCommand.Build(executor));
        cmd.Add(DictionaryGetCommand.Build(executor));
        cmd.Add(DictionaryCreateCommand.Build(executor));
        cmd.Add(DictionaryUpdateCommand.Build(executor));
        cmd.Add(DictionaryMoveCommand.Build(executor));
        cmd.Add(DictionaryDeleteCommand.Build(executor));
        return cmd;
    }
}
