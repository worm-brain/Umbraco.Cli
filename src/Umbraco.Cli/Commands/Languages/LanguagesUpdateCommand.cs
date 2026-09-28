using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Languages;

/// <summary>Wires the <c>language update</c> command (issue #59).</summary>
public static class LanguagesUpdateCommand
{
    /// <summary>
    /// Builds the <c>language update</c> command. Only supplied options change; anything
    /// omitted (name, the default/mandatory flags, the fallback culture) is preserved by the
    /// client's read-merge, so a name change never silently clears the other settings.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a language by its ISO culture code. Omitted fields are preserved."
        )
            .WithExamples(
                "umbraco language update fr-FR --name \"French (France)\" --mandatory",
                "umbraco language update da-DK --fallback en-US"
            )
            .Mutating();
        var isoArg = new Argument<string>("id")
        {
            Description = "The language's ISO code (e.g. fr-FR).",
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "New display name for the language.",
        };
        var defaultOpt = new Option<bool?>("--default")
        {
            Description = "Set/unset this as the default language (--default or --default false).",
        };
        var mandatoryOpt = new Option<bool?>("--mandatory")
        {
            Description = "Set/unset content in this language as mandatory.",
        };
        var fallbackOpt = new Option<string?>("--fallback")
        {
            Description = "ISO code of the fallback language.",
        };
        cmd.Add(isoArg);
        cmd.Add(nameOpt);
        cmd.Add(defaultOpt);
        cmd.Add(mandatoryOpt);
        cmd.Add(fallbackOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.UpdateLanguageAsync(
                            parseResult.GetValue(isoArg)!,
                            new UpdateLanguageRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                IsDefault = parseResult.GetValue(defaultOpt),
                                IsMandatory = parseResult.GetValue(mandatoryOpt),
                                FallbackIsoCode = parseResult.GetValue(fallbackOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
