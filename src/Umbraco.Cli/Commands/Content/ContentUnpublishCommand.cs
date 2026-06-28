using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUnpublishCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("unpublish", "Unpublish a content item, taking it offline. Optionally target specific cultures.\n\nExamples:\n  umbraco content unpublish 3f7a8b2e-...\n  umbraco content unpublish 3f7a8b2e-... --cultures en-US");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures") { Description = "ISO culture codes to unpublish. Unpublishes all cultures if omitted.", AllowMultipleArgumentsPerToken = true  };
        cmd.Add(idArg); cmd.Add(culturesOpt);

        cmd.SetAction((parseResult, ct) =>
        {
            var cultures = parseResult.GetValue(culturesOpt);
            return executor.RunMessageAsync(
                parseResult, "content.unpublish",
                (client, c) => client.UnpublishContentAsync(parseResult.GetValue(idArg), cultures?.Length > 0 ? cultures : null, c),
                "Content item unpublished.",
                ct);
        });

        return cmd;
    }
}
