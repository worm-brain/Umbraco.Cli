using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentPublishCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("publish", "Publish a content item, making it live. Optionally target specific cultures.\n\nExamples:\n  umbraco content publish 3f7a8b2e-...\n  umbraco content publish 3f7a8b2e-... --cultures en-US da-DK");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures") { Description = "ISO culture codes to publish (e.g. en-US da-DK). Publishes all cultures if omitted.", AllowMultipleArgumentsPerToken = true  };
        cmd.Add(idArg); cmd.Add(culturesOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.publish", ct); }
            catch (OperationCanceledException) { return 2; }

            var cultures = parseResult.GetValue(culturesOpt);
            var result = await ctx.Client.PublishContentAsync(parseResult.GetValue(idArg), cultures?.Length > 0 ? cultures : null, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Content item published.");
            return 0;
        });

        return cmd;
    }
}
