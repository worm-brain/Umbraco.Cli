using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Languages;

/// <summary>Wires <c>language delete</c>.</summary>
public static class LanguagesDeleteCommand
{
    /// <summary>
    /// Builds <c>language delete</c>. It always needs <c>--force</c> (#269): Umbraco deletes every
    /// culture variant and dictionary translation in the language, and nothing says whether there
    /// are any.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a language by its ISO code.\n\n"
                + "Always needs --force: Umbraco deletes every culture variant and dictionary translation in the language with it."
        )
            .WithExamples("umbraco language delete fr-FR --force --yes")
            .Mutating();
        var isoArg = new Argument<string>("id")
        {
            Description = "The language's ISO code (e.g. en-US, fr-FR).",
        };
        cmd.Add(isoArg);
        // The same check a prune of a language runs (#281): a language delete always has a reason.
        InUseGuard.Protect(
            cmd,
            parseResult => new DeleteTarget.Language(parseResult.GetValue(isoArg)!),
            "Delete the language, and every culture variant and dictionary translation in it."
        );
        cmd.Destructive(parseResult =>
            $"Delete language '{parseResult.GetValue(isoArg)}'? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteLanguageAsync(parseResult.GetValue(isoArg)!, c)
                            .Then(ItemRef.Of(parseResult.GetValue(isoArg))),
                    "Language removed.",
                    ct
                )
        );

        return cmd;
    }
}
