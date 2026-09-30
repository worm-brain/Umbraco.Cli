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
    /// <summary>
    /// Builds the command. Translations come from <c>--value</c> and <c>--value-file</c>
    /// (<see cref="DictionaryTranslationInput"/>); the files are read inside the executor call.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a dictionary item's translations.\n\nTranslations are merged by ISO code, so naming one language leaves the others alone. Use full ISO codes (en-US, not en) - Umbraco discards codes it does not recognise, so unknown ones are refused here rather than silently dropped."
        )
            .WithExamples(
                "umbraco dictionary update Blog.MinRead --value da-DK=Hjem",
                "umbraco dictionary update Nav.Home --key \"Nav.HomePage\" --value en-US=Home",
                "umbraco dictionary update Blog.Intro --value-file en-US=intro.md --value da-DK=Hej",
                "cat intro.md | umbraco dictionary update Blog.Intro --value-file en-US=-"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.DictionaryItem);
        var keyOpt = new Option<string?>("--key")
        {
            Description = "New key/name for the item. Omit to keep the current one.",
        };
        var valuesOpt = new Option<string[]>("--value")
        {
            Description =
                "Translation pairs in isoCode=value format. Repeat for multiple languages: --value en-US=Home --value da-DK=Hjem. The value is stored as given, in whatever format the site uses; 'dictionary get' reports that format as meta.valueFormat. An empty value (da-DK=) clears that translation.",
            AllowMultipleArgumentsPerToken = true,
        };
        var valueFilesOpt = DictionaryTranslationInput.FileOption();
        cmd.Add(idArg);
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);
        cmd.Add(valueFilesOpt);

        cmd.Validators.Add(result =>
        {
            if (
                string.IsNullOrEmpty(result.GetValue(keyOpt))
                && (result.GetValue(valuesOpt) ?? []).Length == 0
                && (result.GetValue(valueFilesOpt) ?? []).Length == 0
            )
                result.AddError(
                    "Supply --key, --value or --value-file; there is nothing to update."
                );
        });
        KeyValuePairs.Validate(
            cmd,
            valuesOpt,
            "--value must be isoCode=translation, e.g. en-US=Home"
        );
        DictionaryTranslationInput.Validate(cmd, valuesOpt, valueFilesOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // Read the files before resolving the key, so a missing one costs no request.
                        var translations = await DictionaryTranslationInput.ReadAsync(
                            parseResult,
                            valuesOpt,
                            valueFilesOpt,
                            c
                        );
                        return await idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client.UpdateDictionaryItemAsync(
                                    id,
                                    new UpdateDictionaryItemRequest
                                    {
                                        Name = parseResult.GetValue(keyOpt),
                                        Translations = translations,
                                    },
                                    c
                                ),
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }
}
