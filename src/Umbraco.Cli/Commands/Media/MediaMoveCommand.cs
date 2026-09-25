using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media move</c> command (issue #67).</summary>
public static class MediaMoveCommand
{
    /// <summary>Builds the <c>media move</c> command (move a media item under a new folder).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "move",
            "Move a media item under a new parent folder.\n\nExamples:\n  umbraco media move 3f7a8b2e-... --parent 1a2b3c4d-...\n  umbraco media move 3f7a8b2e-...   # to the media root"
        );
        var idArg = new Argument<Guid>("id") { Description = "Media item ID to move." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Target folder ID. Moves to the media root if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.move",
                    (client, c) =>
                        client.MoveMediaAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt),
                            c
                        ),
                    "Media moved.",
                    ct
                )
        );

        return cmd;
    }
}
