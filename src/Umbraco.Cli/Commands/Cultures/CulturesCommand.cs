using System.CommandLine;

namespace Umbraco.Cli.Commands.Cultures;

/// <summary>Wires the read-only <c>culture</c> noun (issue #107).</summary>
public static class CulturesCommand
{
    /// <summary>Builds the <c>culture</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "culture",
            "List the cultures available on the instance.\n\nExamples:\n  umbraco culture list"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List available cultures.\n\nExamples:\n  umbraco culture list\n  umbraco culture list --take 500 --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
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
