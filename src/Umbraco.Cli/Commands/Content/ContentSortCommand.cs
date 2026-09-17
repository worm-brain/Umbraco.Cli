using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content sort</c> command (issue #88).</summary>
public static class ContentSortCommand
{
    /// <summary>Builds the <c>content sort</c> command (reorder a parent's child content items).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Reorder a parent's child content items. The children are set to the given order (first = top).\n\nExamples:\n  umbraco content sort --parent 1a2b3c4d-... --children 3f7a... 9c4d... 2e6f...\n  umbraco content sort --children 3f7a... 9c4d...   # reorder items at the content root"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent ID whose children to reorder. Reorders the content root if omitted.",
        };
        // Ordered, multi-valued: the position in --children becomes the sort order (index 0, 1, ...).
        var childrenOpt = new Option<Guid[]>("--children")
        {
            Description = "Child content IDs in the desired order (first gets sort order 0).",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        cmd.Add(parentOpt);
        cmd.Add(childrenOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content.sort",
                    (client, c) =>
                        client.SortContentAsync(
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(childrenOpt) ?? [],
                            c
                        ),
                    "Content children reordered.",
                    ct
                )
        );

        return cmd;
    }
}
