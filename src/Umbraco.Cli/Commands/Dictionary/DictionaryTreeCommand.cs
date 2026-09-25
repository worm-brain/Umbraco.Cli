using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary tree</c> command (issue #110).</summary>
public static class DictionaryTreeCommand
{
    /// <summary>Builds the <c>dictionary tree</c> command (browse the dictionary hierarchy level by level).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "tree",
            "Browse the dictionary hierarchy. Lists the root level, or the direct children of --parent.\n\nExamples:\n  umbraco dictionary tree\n  umbraco dictionary tree --parent Blog --output json"
        );
        var parentOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The item whose direct children to list; lists the root level if omitted"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "dictionary.tree",
                    (client, skip, take, c) =>
                        client.WithResolvedOptionalAsync(
                            EntityKind.DictionaryItem,
                            parseResult.GetValue(parentOpt),
                            parent => client.GetDictionaryTreeAsync(parent, skip, take, c),
                            c
                        ),
                    ["ID", "Name", "Parent ID", "Has Children"],
                    i =>
                        new[]
                        {
                            i.Id.ToString(),
                            i.Name,
                            i.Parent?.Id.ToString() ?? "",
                            i.HasChildren ? "yes" : "no",
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
