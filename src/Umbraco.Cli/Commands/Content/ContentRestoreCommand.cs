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
    /// and <c>--to-root</c> forces the content root. The item comes back unpublished and last in
    /// its parent's sort order; <c>--publish</c> publishes it afterwards (#233).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "restore",
            "Restore a content item from the recycle bin.\n\nThe item comes back unpublished and last in its parent's sort order. Pass --publish to publish it, and use 'content sort' to reorder."
        )
            .WithExamples(
                "umbraco content restore 3f7a8b2e-...",
                "umbraco content restore 3f7a8b2e-... --parent 1a2b3c4d-...",
                "umbraco content restore 3f7a8b2e-... --to-root",
                "umbraco content restore 3f7a8b2e-... --publish"
            )
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Trashed content item ID." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Parent to restore under. Restores to the original parent if omitted.",
        };
        var toRootOpt = new Option<bool>("--to-root")
        {
            Description = "Restore to the content root instead of the original parent.",
        };
        var publishOpt = new Option<bool>("--publish")
        {
            Description = "Publish the item after restoring it (every culture it has).",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.Add(toRootOpt);
        cmd.Add(publishOpt);
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
            {
                var id = parseResult.GetValue(idArg);
                var publish = parseResult.GetValue(publishOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                    {
                        var restore = client.RestoreContentAsync(
                            id,
                            parseResult.GetValue(parentOpt) is { } parent
                                    ? RestoreTarget.Under(parent)
                                : parseResult.GetValue(toRootOpt) ? RestoreTarget.Root
                                : RestoreTarget.Original,
                            c
                        );
                        return (
                            publish
                                ? restore.ThenPublishAsync(client, id, null, "Restored", c)
                                : restore
                        ).Then(ItemRef.Of(id));
                    },
                    publish
                        ? "Content restored from the recycle bin and published."
                        : "Content restored from the recycle bin (unpublished).",
                    ct
                );
            }
        );

        return cmd;
    }
}
