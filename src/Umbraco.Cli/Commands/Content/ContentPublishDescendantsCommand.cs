using System.CommandLine;
using Umbraco.Cli.Infrastructure;

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
            "Publish a content item and its descendants.\n\nExamples:\n  umbraco content publish-descendants 3f7a8b2e-...\n  umbraco content publish-descendants 3f7a8b2e-... --include-unpublished --culture en-US"
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Root content item ID." };
        var culturesOpt = ListOption.Strings(
            "--culture",
            "ISO culture codes to publish. Publishes all cultures if omitted."
        );
        var includeUnpublishedOpt = new Option<bool>("--include-unpublished")
        {
            DefaultValueFactory = _ => false,
            Description = "Also publish descendants that have never been published.",
        };
        var waitOpt = new Option<bool>("--wait")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Poll the background publish task until it completes, rather than returning as soon as it is queued (#90).",
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);
        cmd.Add(includeUnpublishedOpt);
        cmd.Add(waitOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                // Object output (rather than a fixed message) surfaces the background task id and
                // completion state, so a script can chain on --wait or poll the id itself (#90).
                return executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.PublishContentWithDescendantsAsync(
                            parseResult.GetValue(idArg),
                            cultures?.Length > 0 ? cultures : null,
                            parseResult.GetValue(includeUnpublishedOpt),
                            parseResult.GetValue(waitOpt),
                            c
                        ),
                    ct
                );
            }
        );

        return cmd;
    }
}
