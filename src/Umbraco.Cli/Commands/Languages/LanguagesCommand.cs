using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "language",
            "Manage the languages configured in the Umbraco instance."
        ).WithExamples(
            "umbraco language list",
            "umbraco language create --culture fr-FR",
            "umbraco language delete fr-FR"
        );
        cmd.Add(LanguagesListCommand.Build(executor));
        cmd.Add(LanguagesCreateCommand.Build(executor));
        cmd.Add(LanguagesUpdateCommand.Build(executor));
        cmd.Add(LanguagesDeleteCommand.Build(executor));
        return cmd;
    }
}
