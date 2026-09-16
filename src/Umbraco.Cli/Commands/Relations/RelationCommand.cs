using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Relations;

/// <summary>
/// Wires the read-only <c>relation</c> noun (issue #118). The generated client lists relations only
/// by relation-type id, so <c>list</c> requires <c>--type</c>.
/// </summary>
public static class RelationCommand
{
    /// <summary>Builds the <c>relation</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "relation",
            "List relations of a relation type.\n\nExample:\n  umbraco relation list --type <relationTypeId>"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List the relations of a relation type.");
        var typeOpt = new Option<Guid>("--type")
        {
            Required = true,
            Description = "The relation type ID to list relations for.",
        };
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 100 };
        cmd.Add(typeOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "relation.list",
                    (client, c) =>
                        client.GetRelationsByTypeAsync(
                            parseResult.GetValue(typeOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Parent", "Child", "Comment" },
                    data =>
                        (data?.Items ?? []).Select(r =>
                            new[] { r.ParentName, r.ChildName, r.Comment ?? "" }
                        ),
                    ct
                )
        );
        return cmd;
    }
}
