using System.CommandLine;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "languages",
            "Manage the languages configured in the Umbraco instance.\n\nExamples:\n  umbraco languages list\n  umbraco languages create --culture fr-FR\n  umbraco languages delete fr-FR"
        );
        cmd.Add(LanguagesListCommand.Build(executor));
        cmd.Add(LanguagesCreateCommand.Build(executor));
        cmd.Add(LanguagesUpdateCommand.Build(executor));
        cmd.Add(LanguagesDeleteCommand.Build(executor));
        return cmd;
    }
}
