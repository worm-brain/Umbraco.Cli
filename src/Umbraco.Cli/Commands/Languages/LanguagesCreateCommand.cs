using System.CommandLine;
using System.Globalization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Languages;

/// <summary>Wires the <c>languages create</c> command.</summary>
public static class LanguagesCreateCommand
{
    /// <summary>
    /// Builds the <c>languages create</c> command. The API requires a human-readable
    /// <c>name</c> (issue #47); when <c>--name</c> is omitted it is derived from the
    /// culture's display name so the common case stays a one-flag call.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Add a language.\n\nExamples:\n  umbraco languages create --culture da-DK\n  umbraco languages create --culture da-DK --fallback en-US --mandatory"
        );
        var cultureOpt = new Option<string>("--culture")
        {
            Description = "ISO 4646 culture code for the language (e.g. en-US, fr-FR, da-DK).",
            Required = true,
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description =
                "Display name for the language. Defaults to the culture's display name (e.g. 'French (France)').",
        };
        var defaultOpt = new Option<bool>("--default")
        {
            DefaultValueFactory = _ => false,
            Description = "Set this language as the default for new content.",
        };
        var mandatoryOpt = new Option<bool>("--mandatory")
        {
            DefaultValueFactory = _ => false,
            Description = "Mark this language as mandatory (content must be translated into it).",
        };
        var fallbackOpt = new Option<string?>("--fallback")
        {
            Description =
                "ISO code of the language to fall back to when content has no translation in this one (e.g. en-US).",
        };
        cmd.Add(cultureOpt);
        cmd.Add(nameOpt);
        cmd.Add(defaultOpt);
        cmd.Add(mandatoryOpt);
        cmd.Add(fallbackOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                    {
                        var culture = parseResult.GetValue(cultureOpt)!;
                        return client.CreateLanguageAsync(
                            new CreateLanguageRequest
                            {
                                IsoCode = culture,
                                Name = parseResult.GetValue(nameOpt) ?? DeriveName(culture),
                                IsDefault = parseResult.GetValue(defaultOpt),
                                IsMandatory = parseResult.GetValue(mandatoryOpt),
                                // #183: `update` always had this, so setting up a language took
                                // two commands.
                                FallbackIsoCode = parseResult.GetValue(fallbackOpt),
                            },
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Derives a display name from a culture code, falling back to the raw code when the
    /// culture is not recognised by the .NET globalization data.
    /// </summary>
    /// <param name="culture">The ISO culture code (e.g. "fr-FR").</param>
    /// <returns>The culture's display name, or the code itself if unknown.</returns>
    private static string DeriveName(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return culture;
        }
    }
}
