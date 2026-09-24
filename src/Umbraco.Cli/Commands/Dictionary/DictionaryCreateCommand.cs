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
            "Create a new dictionary item with translations.\n\nExamples:\n  umbraco dictionary create --key \"Common.Search\"\n  umbraco dictionary create --key \"Nav.Home\" --values en=Home --values da=Hjem --values fr=Accueil"
        );
        var keyOpt = new Option<string>("--key") { Required = true };
        // --values accepts en=Hello da=Hej style pairs
        var valuesOpt = new Option<string[]>("--values")
        {
            Description =
                "Translation pairs in lang=value format. Repeat for multiple languages: --values en=Home --values da=Hjem",
            AllowMultipleArgumentsPerToken = true,
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Optional parent item ID to create this item under. Creates at the root if omitted (#110).",
        };
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);
        cmd.Add(idOpt);
        cmd.Add(parentOpt);
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

                var parent = parseResult.GetValue(parentOpt);
                return executor.RunObjectAsync(
                    parseResult,
                    "dictionary.create",
                    (client, c) =>
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
                    ct
                );
            }
        );

        return cmd;
    }
}
