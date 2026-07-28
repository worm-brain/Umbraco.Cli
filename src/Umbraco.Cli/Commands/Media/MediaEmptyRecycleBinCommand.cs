using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media empty-recycle-bin</c> command (issue #67).</summary>
public static class MediaEmptyRecycleBinCommand
{
    /// <summary>
    /// Builds the <c>media empty-recycle-bin</c> command. Permanently deletes every trashed
    /// media item, so it is gated by a confirmation prompt (requires <c>--yes</c>
    /// non-interactively).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "empty-recycle-bin",
            "Permanently delete all media items in the recycle bin.\n\nExample:\n  umbraco media empty-recycle-bin --yes"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.empty-recycle-bin",
                    (client, c) => client.EmptyMediaRecycleBinAsync(c),
                    "Media recycle bin emptied.",
                    ct,
                    confirmationPrompt: "Permanently delete ALL items in the media recycle bin? This cannot be undone."
                )
        );

        return cmd;
    }
}
