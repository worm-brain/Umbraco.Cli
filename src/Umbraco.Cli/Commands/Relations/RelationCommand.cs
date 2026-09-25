using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Relations;

/// <summary>
/// Wires the read-only <c>relation</c> noun (issue #118). The generated client lists relations only
/// by relation-type id, so <c>list</c> requires <c>--relation-type</c>.
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
            "List relations of a relation type.\n\nExample:\n  umbraco relation list --relation-type <relationTypeId>"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List the relations of a relation type.");
        var typeOpt = new Option<Guid>("--relation-type")
        {
            Required = true,
            Description = "The relation type ID to list relations for.",
        };
        cmd.Add(typeOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetRelationsByTypeAsync(
                            parseResult.GetValue(typeOpt),
                            skip,
                            take,
                            c
                        ),
                    new[] { "Parent", "Child", "Comment" },
                    r => new[] { r.ParentName, r.ChildName, r.Comment ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }
}
