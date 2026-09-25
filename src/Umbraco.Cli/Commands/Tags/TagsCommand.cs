using System.CommandLine;

namespace Umbraco.Cli.Commands.Tags;

/// <summary>Wires the read-only <c>tag</c> noun (issue #107).</summary>
public static class TagsCommand
{
    /// <summary>Builds the <c>tag</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tag",
            "List tags, optionally filtered by group and culture.\n\nExamples:\n  umbraco tag list\n  umbraco tag list --group default --culture en-US"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List tags.");
        var groupOpt = new Option<string?>("--group")
        {
            Description = "Tag group to filter by; omit for all groups.",
        };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "Culture to filter by (e.g. en-US); omit for the default.",
        };
        cmd.Add(groupOpt);
        cmd.Add(cultureOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetTagsAsync(
                            parseResult.GetValue(groupOpt),
                            parseResult.GetValue(cultureOpt),
                            skip,
                            take,
                            c
                        ),
                    new[] { "Text", "Group", "Nodes", "Id" },
                    t => new[] { t.Text, t.Group, t.NodeCount.ToString(), t.Id.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }
}
