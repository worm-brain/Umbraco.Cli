using System.CommandLine;

namespace Umbraco.Cli.Commands.Tags;

/// <summary>Wires the read-only <c>tags</c> noun (issue #107).</summary>
public static class TagsCommand
{
    /// <summary>Builds the <c>tags</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tags",
            "List tags, optionally filtered by group and culture.\n\nExamples:\n  umbraco tags list\n  umbraco tags list --group default --culture en-US"
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
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 100 };
        cmd.Add(groupOpt);
        cmd.Add(cultureOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "tags.list",
                    (client, c) =>
                        client.GetTagsAsync(
                            parseResult.GetValue(groupOpt),
                            parseResult.GetValue(cultureOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Text", "Group", "Nodes", "Id" },
                    data =>
                        (data?.Items ?? []).Select(t =>
                            new[] { t.Text, t.Group, t.NodeCount.ToString(), t.Id.ToString() }
                        ),
                    ct
                )
        );
        return cmd;
    }
}
