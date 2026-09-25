using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content restore</c> command (issue #67).</summary>
public static class ContentRestoreCommand
{
    /// <summary>Builds the <c>content restore</c> command (restore a trashed item from the recycle bin).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "restore",
            "Restore a content item from the recycle bin.\n\nExamples:\n  umbraco content restore 3f7a8b2e-...\n  umbraco content restore 3f7a8b2e-... --parent 1a2b3c4d-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Trashed content item ID." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Parent to restore under. Restores to the content root if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content.restore",
                    (client, c) =>
                        client.RestoreContentAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt),
                            c
                        ),
                    "Content restored from the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
