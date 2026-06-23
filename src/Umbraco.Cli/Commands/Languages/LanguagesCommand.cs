using System.CommandLine;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("languages", "Manage the languages configured in the Umbraco instance.\n\nExamples:\n  umbraco languages list\n  umbraco languages create --culture fr-FR\n  umbraco languages delete fr-FR");
        cmd.Add(LanguagesListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(LanguagesCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(LanguagesDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
