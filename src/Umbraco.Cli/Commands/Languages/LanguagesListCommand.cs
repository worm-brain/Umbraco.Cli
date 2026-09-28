using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all languages configured in the Umbraco instance."
        ).WithExamples("umbraco language list --output json");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
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
