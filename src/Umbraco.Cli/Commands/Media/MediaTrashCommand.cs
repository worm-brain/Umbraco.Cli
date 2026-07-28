using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media trash</c> command (issue #67).</summary>
public static class MediaTrashCommand
{
    /// <summary>
    /// Builds the <c>media trash</c> command. Moves the item to the recycle bin (reversible via
    /// <c>media restore</c>), so it is not gated by a confirmation prompt.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "trash",
            "Move a media item to the recycle bin (reversible with 'media restore').\n\nExample:\n  umbraco media trash 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Media item ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.trash",
                    (client, c) => client.TrashMediaAsync(parseResult.GetValue(idArg), c),
                    "Media moved to the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
