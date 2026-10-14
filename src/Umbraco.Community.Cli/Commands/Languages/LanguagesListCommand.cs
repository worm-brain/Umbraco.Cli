using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Languages;

/// <summary>Wires the <c>language list</c> command.</summary>
public static class LanguagesListCommand
{
    /// <summary>Builds the <c>language list</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
