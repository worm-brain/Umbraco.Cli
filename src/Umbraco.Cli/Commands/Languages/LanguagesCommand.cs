using System.CommandLine;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "language",
            "Manage the languages configured in the Umbraco instance.\n\nExamples:\n  umbraco language list\n  umbraco language create --culture fr-FR\n  umbraco language delete fr-FR"
        );
        cmd.Add(LanguagesListCommand.Build(executor));
        cmd.Add(LanguagesCreateCommand.Build(executor));
        cmd.Add(LanguagesUpdateCommand.Build(executor));
        cmd.Add(LanguagesDeleteCommand.Build(executor));
        return cmd;
    }
}
