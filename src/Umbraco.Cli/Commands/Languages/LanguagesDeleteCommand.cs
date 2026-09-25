using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Remove a language by its ISO culture code.\n\nExample:\n  umbraco language delete fr-FR"
        ).Mutating();
        var isoArg = new Argument<string>("iso-code")
        {
            Description = "ISO culture code of the language to remove (e.g. en-US, fr-FR).",
        };
        cmd.Add(isoArg);
        cmd.Destructive(parseResult =>
            $"Delete language '{parseResult.GetValue(isoArg)}'? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.DeleteLanguageAsync(parseResult.GetValue(isoArg)!, c),
                    "Language removed.",
                    ct
                )
        );

        return cmd;
    }
}
