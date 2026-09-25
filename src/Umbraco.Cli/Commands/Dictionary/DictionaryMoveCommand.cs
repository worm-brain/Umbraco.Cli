using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
            "Move a dictionary item under a new parent.\n\nExamples:\n  umbraco dictionary move Blog.Tags --parent Blog\n  umbraco dictionary move Blog.Tags   # to the dictionary root"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.DictionaryItem);
        var targetOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The new parent; moves to the dictionary root if omitted",
            "--target"
        );
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                targetOpt.WithResolvedOptionalAsync(
                                    parseResult,
                                    client,
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
