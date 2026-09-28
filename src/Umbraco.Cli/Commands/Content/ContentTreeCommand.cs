using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content tree</c> command (issue #89).</summary>
public static class ContentTreeCommand
{
    /// <summary>Builds the <c>content tree</c> command (walk the content tree into a flat list).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tree",
            "Walk the content tree into a flat list of nodes.\n\nEach node carries its depth and parent id."
        ).WithExamples(
            "umbraco content tree                       # direct children of the root",
            "umbraco content tree --parent <id> --recursive",
            "umbraco content tree --depth 3 --output json"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Node whose subtree to walk. Walks from the content root if omitted.",
        };
        var recursiveOpt = new Option<bool>("--recursive")
        {
            Description =
                "Descend all levels, up to a safety cap of 50. Without it, only direct children are listed.",
        };
        var depthOpt = new Option<int?>("--depth")
        {
            Description =
                "Maximum levels to descend (1 = direct children, capped at 50). Overrides --recursive when given.",
        };
        cmd.Add(parentOpt);
        cmd.Add(recursiveOpt);
        cmd.Add(depthOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                // --depth wins if given; else --recursive walks the whole subtree (client clamps
                // to a safety cap); else a single level of direct children.
                var depth = parseResult.GetValue(depthOpt);
                var recursive = parseResult.GetValue(recursiveOpt);
                var maxDepth = depth ?? (recursive ? int.MaxValue : 1);

                return executor.RunCompleteListAsync(
                    parseResult,
                    (client, c) =>
                        client.GetContentTreeAsync(parseResult.GetValue(parentOpt), maxDepth, c),
                    ["ID", "Name", "Parent ID", "Depth", "Has Children"],
                    i =>
                        new[]
                        {
                            i.Id.ToString(),
                            i.Name,
                            i.ParentId?.ToString() ?? "",
                            i.Depth.ToString(),
                            i.HasChildren ? "yes" : "no",
                        },
                    ct
                );
            }
        );

        return cmd;
    }
}
