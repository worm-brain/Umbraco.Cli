using System.CommandLine;

namespace Umbraco.Cli.Commands.Cultures;

/// <summary>Wires the read-only <c>cultures</c> noun (issue #107).</summary>
public static class CulturesCommand
{
    /// <summary>Builds the <c>cultures</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "cultures",
            "List the cultures available on the instance.\n\nExample:\n  umbraco cultures list"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List available cultures.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "cultures.list",
                    (client, skip, take, c) => client.GetCulturesAsync(skip, take, c),
                    new[] { "ISO Code", "English Name" },
                    c => new[] { c.IsoCode, c.EnglishName },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }
}
