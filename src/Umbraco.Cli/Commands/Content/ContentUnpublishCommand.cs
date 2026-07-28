using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content unpublish</c> command.</summary>
public static class ContentUnpublishCommand
{
    /// <summary>
    /// Builds the <c>content unpublish</c> command. Unpublishing takes live content offline, a
    /// high production-impact action, so it is gated by a confirmation prompt (requires
    /// <c>--yes</c> non-interactively) even though it is reversible by re-publishing (issue #82).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "unpublish",
            "Unpublish a content item, taking it offline. Optionally target specific cultures.\n\nExamples:\n  umbraco content unpublish 3f7a8b2e-...\n  umbraco content unpublish 3f7a8b2e-... --cultures en-US"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures")
        {
            Description = "ISO culture codes to unpublish. Unpublishes all cultures if omitted.",
            AllowMultipleArgumentsPerToken = true,
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    "content.unpublish",
                    (client, c) =>
                        client.UnpublishContentAsync(
                            parseResult.GetValue(idArg),
                            cultures?.Length > 0 ? cultures : null,
                            c
                        ),
                    "Content item unpublished.",
                    ct,
                    confirmationPrompt: $"Unpublish content {parseResult.GetValue(idArg)}, taking it offline? Re-publish to restore."
                );
            }
        );

        return cmd;
    }
}
