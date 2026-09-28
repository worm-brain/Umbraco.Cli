using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary delete</c> command (issue #59).</summary>
public static class DictionaryDeleteCommand
{
    /// <summary>Builds the <c>dictionary delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a dictionary item by id or key.\n\n"
                + "An item with child items is refused unless --force is given: Umbraco deletes the children with it."
        )
            .WithExamples(
                "umbraco dictionary delete Blog.MinRead --yes",
                "umbraco dictionary delete Blog --force --yes"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.DictionaryItem);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            idArg,
            "Delete even though the item has child items, deleting them and their translations too."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete dictionary item {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteDictionaryItemAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Dictionary item deleted.",
                    ct
                )
        );

        return cmd;
    }
}
