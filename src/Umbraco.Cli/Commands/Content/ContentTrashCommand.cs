using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content trash</c> command (issue #67).</summary>
public static class ContentTrashCommand
{
    /// <summary>
    /// Builds the <c>content trash</c> command. Unlike <c>content delete</c> (permanent), this
    /// moves the item to the recycle bin and is reversible via <c>content restore</c>, so it is
    /// not gated by a confirmation prompt.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "trash",
            "Move a content item to the recycle bin (reversible with 'content restore')."
        )
            .WithExamples("umbraco content trash 3f7a8b2e-...")
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .TrashContentAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Content moved to the recycle bin.",
                    ct
                )
        );

        return cmd;
    }
}
