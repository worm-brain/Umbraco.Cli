using System.CommandLine;
using Umbraco.Cli.Client;

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
        cmd.Add(keyOpt);
        cmd.Add(valuesOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var translations = (parseResult.GetValue(valuesOpt) ?? [])
                    .Select(v => v.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .Select(p => new DictionaryTranslation { IsoCode = p[0], Translation = p[1] });

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
