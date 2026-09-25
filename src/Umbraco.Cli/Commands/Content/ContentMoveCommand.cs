using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content move</c> command (issue #67).</summary>
public static class ContentMoveCommand
{
    /// <summary>Builds the <c>content move</c> command (move an item under a new parent).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "move",
            "Move a content item under a new parent.\n\nExamples:\n  umbraco content move 3f7a8b2e-... --parent 1a2b3c4d-...\n  umbraco content move 3f7a8b2e-...   # to the content root"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID to move." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Target parent ID. Moves to the content root if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client.MoveContentAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt),
                            c
                        ),
                    "Content moved.",
                    ct
                )
        );

        return cmd;
    }
}
