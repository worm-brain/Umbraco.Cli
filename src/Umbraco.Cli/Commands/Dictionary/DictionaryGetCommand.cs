using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary get</c> command.</summary>
public static class DictionaryGetCommand
{
    /// <summary>
    /// Builds <c>dictionary get &lt;id&gt;</c>: the argument is an id or a key, resolved to the id
    /// and then read by id.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a dictionary item and its translations by key or id.")
            .WithExamples(
                "umbraco dictionary get Common.Search",
                "umbraco dictionary get 1a2b3c4d-....."
            )
            // meta.valueFormat: the format the site stores translations in (#440).
            .WithValueFormat();
        // The key is resolved here, before the by-id read (#262), by the same resolver every
        // other <id|key> argument uses.
        var keyArg = Reference.Argument(EntityKind.DictionaryItem);
        cmd.Add(keyArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        keyArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetDictionaryItemByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
