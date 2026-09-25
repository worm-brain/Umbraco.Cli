using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new dictionary item with translations.\n\nExamples:\n  umbraco dictionary create --key \"Common.Search\"\n  umbraco dictionary create --key \"Blog.MinRead\" --parent Blog\n  umbraco dictionary create --key \"Nav.Home\" --value en-US=Home --value da-DK=Hjem --value fr-FR=Accueil"
        ).Mutating();
        var keyOpt = new Option<string>("--key") { Required = true };
        // --value accepts isoCode=value pairs (en-US=Hello da-DK=Hej); the isoCode must be the full culture code (#181)
        var valuesOpt = new Option<string[]>("--value")
        {
            Description =
                "Translation pairs in isoCode=value format, using the full culture code. Repeat for multiple languages: --value en-US=Home --value da-DK=Hjem",
            AllowMultipleArgumentsPerToken = true,
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        var parentOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The item to create this one under; creates at the root if omitted (#110)"
        );
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);
        cmd.Add(idOpt);
        cmd.Add(parentOpt);
        KeyValuePairs.Validate(
            cmd,
            valuesOpt,
            "--value must be isoCode=translation, e.g. en-US=Home"
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
                    (client, c) =>
                        parentOpt.WithResolvedOptionalAsync(
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
                        ),
                    ct
                );
            }
        );

        return cmd;
    }
}
