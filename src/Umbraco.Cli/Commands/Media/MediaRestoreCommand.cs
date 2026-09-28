using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media restore</c> command (issue #67).</summary>
public static class MediaRestoreCommand
{
    /// <summary>
    /// Builds the <c>media restore</c> command. Like <c>content restore</c> (#265): the item goes
    /// back under its original parent by default; <c>--parent</c> picks another and
    /// <c>--to-root</c> forces the media root.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "restore",
            "Restore a media item from the recycle bin.\n\nThe item comes back last in its parent's sort order; use 'media sort' to reorder."
        )
            .WithExamples(
                "umbraco media restore 3f7a8b2e-...",
                "umbraco media restore 3f7a8b2e-... --parent 1a2b3c4d-...",
                "umbraco media restore 3f7a8b2e-... --to-root"
            )
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Trashed media item ID." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Folder to restore under. Restores to the original parent if omitted.",
        };
        var toRootOpt = new Option<bool>("--to-root")
        {
            Description = "Restore to the media root instead of the original parent.",
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
                        client
                            .RestoreMediaAsync(
                                parseResult.GetValue(idArg),
                                parseResult.GetValue(parentOpt) is { } parent
                                        ? RestoreTarget.Under(parent)
                                    : parseResult.GetValue(toRootOpt) ? RestoreTarget.Root
                                    : RestoreTarget.Original,
                                c
                            )
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Media restored from the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
