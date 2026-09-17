using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media sort</c> command (issue #88).</summary>
public static class MediaSortCommand
{
    /// <summary>Builds the <c>media sort</c> command (reorder a parent folder's child media items).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Reorder a parent folder's child media items. The children are set to the given order (first = top).\n\nExamples:\n  umbraco media sort --parent 1a2b3c4d-... --children 3f7a... 9c4d... 2e6f...\n  umbraco media sort --children 3f7a... 9c4d...   # reorder items at the media root"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent folder ID whose children to reorder. Reorders the media root if omitted.",
        };
        // Ordered, multi-valued: the position in --children becomes the sort order (index 0, 1, ...).
        var childrenOpt = new Option<Guid[]>("--children")
        {
            Description = "Child media IDs in the desired order (first gets sort order 0).",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        cmd.Add(parentOpt);
        cmd.Add(childrenOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.sort",
                    (client, c) =>
                        client.SortMediaAsync(
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(childrenOpt) ?? [],
                            c
                        ),
                    "Media children reordered.",
                    ct
                )
        );

        return cmd;
    }
}
