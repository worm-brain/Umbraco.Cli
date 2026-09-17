using System.CommandLine;

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
            "Move a dictionary item under a new parent.\n\nExamples:\n  umbraco dictionary move 3f7a8b2e-... --target 1a2b3c4d-...\n  umbraco dictionary move 3f7a8b2e-...   # to the dictionary root"
        );
        var idArg = new Argument<Guid>("id") { Description = "Dictionary item ID to move." };
        var targetOpt = new Option<Guid?>("--target")
        {
            Description = "Target parent ID. Moves to the dictionary root if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "dictionary.move",
                    (client, c) =>
                        client.MoveDictionaryItemAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(targetOpt),
                            c
                        ),
                    "Dictionary item moved.",
                    ct
                )
        );

        return cmd;
    }
}
