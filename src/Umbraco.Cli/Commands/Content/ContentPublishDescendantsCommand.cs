using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content publish-descendants</c> command (issue #67).</summary>
public static class ContentPublishDescendantsCommand
{
    /// <summary>Builds the <c>content publish-descendants</c> command (publish a branch).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "publish-descendants",
            "Publish a content item and its descendants.\n\nExamples:\n  umbraco content publish-descendants 3f7a8b2e-...\n  umbraco content publish-descendants 3f7a8b2e-... --include-unpublished --cultures en-US"
        );
        var idArg = new Argument<Guid>("id") { Description = "Root content item ID." };
        var culturesOpt = new Option<string[]>("--cultures")
        {
            Description = "ISO culture codes to publish. Publishes all cultures if omitted.",
            AllowMultipleArgumentsPerToken = true,
        };
        var includeUnpublishedOpt = new Option<bool>("--include-unpublished")
        {
            DefaultValueFactory = _ => false,
            Description = "Also publish descendants that have never been published.",
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);
        cmd.Add(includeUnpublishedOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    "content.publish-descendants",
                    (client, c) =>
                        client.PublishContentWithDescendantsAsync(
                            parseResult.GetValue(idArg),
                            cultures?.Length > 0 ? cultures : null,
                            parseResult.GetValue(includeUnpublishedOpt),
                            c
                        ),
                    "Content and descendants published.",
                    ct
                );
            }
        );

        return cmd;
    }
}
