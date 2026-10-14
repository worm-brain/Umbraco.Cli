using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Languages;

/// <summary>Wires the <c>language</c> noun: the cultures content can vary by.</summary>
public static class LanguagesCommand
{
    /// <summary>Builds the <c>language</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
