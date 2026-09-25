using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content restore</c> command (issue #67).</summary>
public static class ContentRestoreCommand
{
    /// <summary>
    /// Builds the <c>content restore</c> command (restore a trashed item from the recycle bin). By
    /// default the item goes back under its original parent (#230); <c>--parent</c> picks another
    /// and <c>--to-root</c> forces the content root.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "restore",
            "Restore a content item from the recycle bin.\n\nExamples:\n  umbraco content restore 3f7a8b2e-...\n  umbraco content restore 3f7a8b2e-... --parent 1a2b3c4d-...\n  umbraco content restore 3f7a8b2e-... --to-root"
        );
        var idArg = new Argument<Guid>("id") { Description = "Trashed content item ID." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Parent to restore under. Restores to the original parent if omitted.",
        };
        var toRootOpt = new Option<bool>("--to-root")
        {
            Description = "Restore to the content root instead of the original parent.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.Add(toRootOpt);
        cmd.Validators.Add(result =>
        {
            if (
                result.GetValue(toRootOpt)
                && CommandValidation.TryGetValue(result, parentOpt, out var parent)
                && parent is not null
            )
                result.AddError("Use --parent or --to-root, not both.");
        });
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client.RestoreContentAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt) is { } parent
                                    ? RestoreTarget.Under(parent)
                                : parseResult.GetValue(toRootOpt) ? RestoreTarget.Root
                                : RestoreTarget.Original,
                            c
                        ),
                    "Content restored from the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
