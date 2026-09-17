using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

public static class ContentPublishCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "publish",
            "Publish a content item, making it live. Optionally target specific cultures.\n\nExamples:\n  umbraco content publish 3f7a8b2e-...\n  umbraco content publish 3f7a8b2e-... --cultures en-US da-DK"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures")
        {
            Description =
                "ISO culture codes to publish (e.g. en-US da-DK). Publishes all cultures if omitted.",
            AllowMultipleArgumentsPerToken = true,
        };
        var publishAtOpt = new Option<DateTimeOffset?>("--publish-at")
        {
            Description =
                "Schedule the publish for a future time (ISO 8601, e.g. 2026-01-01T09:00:00Z). Publishes now if omitted (#90).",
        };
        var unpublishAtOpt = new Option<DateTimeOffset?>("--unpublish-at")
        {
            Description =
                "Schedule an automatic unpublish at a future time (ISO 8601). Stays published if omitted (#90).",
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);
        cmd.Add(publishAtOpt);
        cmd.Add(unpublishAtOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    "content.publish",
                    (client, c) =>
                        client.PublishContentAsync(
                            parseResult.GetValue(idArg),
                            cultures?.Length > 0 ? cultures : null,
                            parseResult.GetValue(publishAtOpt),
                            parseResult.GetValue(unpublishAtOpt),
                            c
                        ),
                    "Content item published.",
                    ct
                );
            }
        );

        return cmd;
    }
}
