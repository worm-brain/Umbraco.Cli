using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires <c>dictionary create</c>.</summary>
public static class DictionaryCreateCommand
{
    /// <summary>
    /// Builds the command. Translations come from <c>--value</c> and <c>--value-file</c>
    /// (<see cref="DictionaryTranslationInput"/>); the files are read inside the executor call.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("create", "Create a new dictionary item with translations.")
            .WithExamples(
                "umbraco dictionary create --key \"Common.Search\"",
                "umbraco dictionary create --key \"Blog.MinRead\" --parent Blog",
                "umbraco dictionary create --key \"Nav.Home\" --value en-US=Home --value da-DK=Hjem --value fr-FR=Accueil",
                "umbraco dictionary create --key \"Blog.Intro\" --value-file en-US=intro.md --value da-DK=Hej"
            )
            .Mutating();
        var keyOpt = new Option<string>("--key")
        {
            Required = true,
            Description = "Key (name) of the new dictionary item, e.g. Nav.Home.",
        };
        // --value accepts isoCode=value pairs (en-US=Hello da-DK=Hej); the isoCode must be the full culture code (#181)
        var valuesOpt = new Option<string[]>("--value")
        {
            Description =
                "Translation pairs in isoCode=value format, using the full ISO code. Repeat for multiple languages: --value en-US=Home --value da-DK=Hjem. The value is stored as given, in whatever format the site uses; 'dictionary get' reports that format as meta.valueFormat. An empty value (en-US=) creates an empty translation.",
            AllowMultipleArgumentsPerToken = true,
        };
        var valueFilesOpt = DictionaryTranslationInput.FileOption();
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
        };
        var parentOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The item to create this one under; creates at the root if omitted"
        );
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);
        cmd.Add(valueFilesOpt);
        cmd.Add(idOpt);
        cmd.Add(parentOpt);
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
                        // Read the files before resolving --parent, so a missing one costs no request.
                        var translations = await DictionaryTranslationInput.ReadAsync(
                            parseResult,
                            valuesOpt,
                            valueFilesOpt,
                            c
                        );
                        return await parentOpt.WithResolvedOptionalAsync(
                            parseResult,
                            client,
                            parent =>
                                client.CreateDictionaryItemAsync(
                                    new CreateDictionaryItemRequest
                                    {
                                        Id = parseResult.GetValue(idOpt),
                                        Name = parseResult.GetValue(keyOpt)!,
                                        Translations = translations,
                                        Parent = parent is { } p
                                            ? new ContentParentReference { Id = p }
                                            : null,
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
