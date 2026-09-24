using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>
/// Wires <c>dictionary update</c> (#182).
/// <para>
/// There was no update verb, so fixing a translation or adding a language meant deleting the item
/// and recreating it - which changed its id, and any reference to it with it.
/// </para>
/// </summary>
public static class DictionaryUpdateCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a dictionary item's translations.\n\nTranslations are merged by ISO code, so naming one language leaves the others alone. Use full ISO codes (en-US, not en) - Umbraco discards codes it does not recognise, so unknown ones are refused here rather than silently dropped (#181).\n\nExamples:\n  umbraco dictionary update 3f7a8b2e-... --values da-DK=Hjem\n  umbraco dictionary update 3f7a8b2e-... --key \"Nav.HomePage\" --values en-US=Home"
        );
        var idArg = new Argument<Guid>("id") { Description = "Dictionary item ID." };
        var keyOpt = new Option<string?>("--key")
        {
            Description = "New key/name for the item. Omit to keep the current one.",
        };
        var valuesOpt = new Option<string[]>("--values")
        {
            Description =
                "Translation pairs in lang=value format. Repeat for multiple languages: --values en-US=Home --values da-DK=Hjem",
            AllowMultipleArgumentsPerToken = true,
        };
        cmd.Add(idArg);
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);

        cmd.Validators.Add(result =>
        {
            if (
                string.IsNullOrEmpty(result.GetValue(keyOpt))
                && (result.GetValue(valuesOpt) ?? []).Length == 0
            )
                result.AddError("Supply --key and/or --values; there is nothing to update.");
        });
        KeyValuePairs.Validate(
            cmd,
            valuesOpt,
            "--values must be isoCode=translation, e.g. en-US=Home"
        );

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var translations = KeyValuePairs
                    .Parse(parseResult.GetValue(valuesOpt))
                    .Select(p => new DictionaryTranslation
                    {
                        IsoCode = p.Key,
                        Translation = p.Value,
                    });

                return executor.RunObjectAsync(
                    parseResult,
                    "dictionary.update",
                    (client, c) =>
                        client.UpdateDictionaryItemAsync(
                            parseResult.GetValue(idArg),
                            new UpdateDictionaryItemRequest
                            {
                                Name = parseResult.GetValue(keyOpt),
                                Translations = translations,
                            },
                            c
                        ),
                    ct
                );
            }
        );

        return cmd;
    }
}
