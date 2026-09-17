using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media tree</c> command (issue #89).</summary>
public static class MediaTreeCommand
{
    /// <summary>Builds the <c>media tree</c> command (walk the media tree into a flat list).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tree",
            "Walk the media tree into a flat list, each node carrying its depth and parent id.\n\nExamples:\n  umbraco media tree                       # direct children of the root\n  umbraco media tree --parent <folder-id> --recursive\n  umbraco media tree --depth 3 --output json"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Folder whose subtree to walk. Walks from the media root if omitted.",
        };
        var recursiveOpt = new Option<bool>("--recursive")
        {
            Description =
                "Descend all levels (bounded for safety). Without it, only direct children are listed.",
        };
        var depthOpt = new Option<int?>("--depth")
        {
            Description =
                "Maximum levels to descend (1 = direct children). Overrides --recursive when given.",
        };
        cmd.Add(parentOpt);
        cmd.Add(recursiveOpt);
        cmd.Add(depthOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var depth = parseResult.GetValue(depthOpt);
                var recursive = parseResult.GetValue(recursiveOpt);
                var maxDepth = depth ?? (recursive ? int.MaxValue : 1);

                return executor.RunTableAsync(
                    parseResult,
                    "media.tree",
                    (client, c) =>
                        client.GetMediaTreeAsync(parseResult.GetValue(parentOpt), maxDepth, c),
                    ["ID", "Name", "Parent ID", "Depth", "Has Children"],
                    data =>
                        data?.Select(i =>
                            new[]
                            {
                                i.Id.ToString(),
                                i.Name,
                                i.ParentId?.ToString() ?? "",
                                i.Depth.ToString(),
                                i.HasChildren ? "yes" : "no",
                            }
                        )
                        ?? [],
                    ct
                );
            }
        );

        return cmd;
    }
}
