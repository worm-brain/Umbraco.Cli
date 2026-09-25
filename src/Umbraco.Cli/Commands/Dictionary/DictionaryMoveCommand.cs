using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary move</c> command (issue #110).</summary>
public static class DictionaryMoveCommand
{
    /// <summary>Builds the <c>dictionary move</c> command (reparent an item under a new target).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "move",
            "Move a dictionary item under a new parent.\n\nExamples:\n  umbraco dictionary move Blog.Tags --target Blog\n  umbraco dictionary move Blog.Tags   # to the dictionary root"
        );
        var idArg = Reference.Argument(EntityKind.DictionaryItem, "key");
        var targetOpt = Reference.Option(
            "--target",
            EntityKind.DictionaryItem,
            "The new parent; moves to the dictionary root if omitted"
        );
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "dictionary.move",
                    (client, c) =>
                        client.WithResolvedAsync(
                            EntityKind.DictionaryItem,
                            parseResult.GetValue(idArg)!,
                            id =>
                                client.WithResolvedOptionalAsync(
                                    EntityKind.DictionaryItem,
                                    parseResult.GetValue(targetOpt),
                                    target => client.MoveDictionaryItemAsync(id, target, c),
                                    c
                                ),
                            c
                        ),
                    "Dictionary item moved.",
                    ct
                )
        );

        return cmd;
    }
}
