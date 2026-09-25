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
            "Delete a dictionary item by id or key.\n\nExample:\n  umbraco dictionary delete Blog.MinRead"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.DictionaryItem, "key");
        cmd.Add(idArg);
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
                            id => client.DeleteDictionaryItemAsync(id, c),
                            c
                        ),
                    "Dictionary item deleted.",
                    ct
                )
        );

        return cmd;
    }
}
