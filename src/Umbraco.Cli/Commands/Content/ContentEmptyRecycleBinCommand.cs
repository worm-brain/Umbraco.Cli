using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content empty-recycle-bin</c> command (issue #67).</summary>
public static class ContentEmptyRecycleBinCommand
{
    /// <summary>
    /// Builds the <c>content empty-recycle-bin</c> command. This permanently deletes every
    /// trashed content item, so it is gated by a confirmation prompt (requires <c>--yes</c>
    /// non-interactively).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "empty-recycle-bin",
            "Permanently delete all content items in the recycle bin.\n\nExample:\n  umbraco content empty-recycle-bin --yes"
        ).Mutating();
        cmd.Destructive(parseResult =>
            "Permanently delete ALL items in the content recycle bin? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.EmptyContentRecycleBinAsync(c),
                    "Content recycle bin emptied.",
                    ct
                )
        );

        return cmd;
    }
}
