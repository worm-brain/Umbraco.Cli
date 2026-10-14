using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary</c> noun: translated static text.</summary>
public static class DictionaryCommand
{
    /// <summary>Builds the <c>dictionary</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
