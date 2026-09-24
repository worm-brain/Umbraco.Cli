using System.CommandLine;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all languages configured in the Umbraco instance.\n\nExample:\n  umbraco languages list --output json"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
                    "languages.list",
                    (client, c) => client.GetLanguagesAsync(c),
                    ["ISO Code", "Name", "Default", "Mandatory"],
                    l =>
                        new[]
                        {
                            l.IsoCode,
                            l.Name,
                            l.IsDefault.ToString(),
                            l.IsMandatory.ToString(),
                        },
                    ct
                )
        );

        return cmd;
    }
}
