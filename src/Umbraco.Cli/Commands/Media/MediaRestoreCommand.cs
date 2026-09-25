using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media restore</c> command (issue #67).</summary>
public static class MediaRestoreCommand
{
    /// <summary>Builds the <c>media restore</c> command (restore a trashed media item).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "restore",
            "Restore a media item from the recycle bin.\n\nExamples:\n  umbraco media restore 3f7a8b2e-...\n  umbraco media restore 3f7a8b2e-... --parent 1a2b3c4d-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Trashed media item ID." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Folder to restore under. Restores to the media root if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client.RestoreMediaAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt),
                            c
                        ),
                    "Media restored from the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
