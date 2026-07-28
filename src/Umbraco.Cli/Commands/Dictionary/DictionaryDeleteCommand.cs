using System.CommandLine;

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
            "Delete a dictionary item by UUID.\n\nExample:\n  umbraco dictionary delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Dictionary item ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "dictionary.delete",
                    (client, c) => client.DeleteDictionaryItemAsync(parseResult.GetValue(idArg), c),
                    "Dictionary item deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete dictionary item {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
