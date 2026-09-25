using System.CommandLine;
using Umbraco.Cli.Infrastructure;

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
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Media item ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .TrashMediaAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Media moved to the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
