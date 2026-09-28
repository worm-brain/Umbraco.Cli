using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Relations;

/// <summary>
/// Wires the read-only <c>relation-type</c> noun (issue #118): list relation types and get one by
/// id or alias. The generated client exposes no create/update/delete for relation types.
/// </summary>
public static class RelationTypeCommand
{
    /// <summary>Builds the <c>relation-type</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "relation-type",
            "List and inspect relation types.\n\nExamples:\n  umbraco relation-type list"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List relation types.\n\nExamples:\n  umbraco relation-type list"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetRelationTypesAsync(skip, take, c),
                    new[] { "Id", "Alias", "Name", "Bidirectional" },
                    t => new[] { t.Id.ToString(), t.Alias, t.Name, t.IsBidirectional.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// Builds <c>relation-type get</c>. The argument takes the id or the alias that
    /// <c>relation-type list</c> shows (#300); it was GUID-only, so the alias was a parse error.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a relation type by id or alias.\n\nExamples:\n  umbraco relation-type get relateDocumentOnCopy\n  umbraco relation-type get 3f7a8b2e-..."
        );
        var idArg = Reference.Argument(EntityKind.RelationType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetRelationTypeByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }
}
