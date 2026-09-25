using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary tree</c> command: a recursive walk, as <c>content tree</c> is.</summary>
public static class DictionaryTreeCommand
{
    /// <summary>
    /// Builds <c>dictionary tree</c>: walks the dictionary into a flat list, each item carrying
    /// its depth and parent, with the same <c>--parent</c> / <c>--recursive</c> / <c>--depth</c>
    /// options as <c>content tree</c> and <c>media tree</c>. Not paged: a walk is complete.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tree",
            "Walk the dictionary into a flat list, each item carrying its depth and parent id.\n\nExamples:\n  umbraco dictionary tree                    # direct children of the root\n  umbraco dictionary tree --parent Blog --recursive\n  umbraco dictionary tree --depth 3 --output json"
        );
        var parentOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The item whose subtree to walk; walks from the root if omitted"
        );
        var recursiveOpt = new Option<bool>("--recursive")
        {
            Description = "Walk the whole subtree instead of one level.",
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
                // --depth wins if given; else --recursive walks the whole subtree (the client
                // clamps to its safety cap); else one level, as content tree does.
                var depth = parseResult.GetValue(depthOpt);
                var maxDepth = depth ?? (parseResult.GetValue(recursiveOpt) ? int.MaxValue : 1);

                return executor.RunCompleteListAsync(
                    parseResult,
                    (client, c) =>
                        parentOpt.WithResolvedOptionalAsync(
                            parseResult,
                            client,
                            parent => client.WalkDictionaryTreeAsync(parent, maxDepth, c),
                            c
                        ),
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
