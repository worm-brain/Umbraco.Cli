using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUnpublishCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("unpublish", "Unpublish a content item, taking it offline. Optionally target specific cultures.\n\nExamples:\n  umbraco content unpublish 3f7a8b2e-...\n  umbraco content unpublish 3f7a8b2e-... --cultures en-US");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures") { Description = "ISO culture codes to unpublish. Unpublishes all cultures if omitted.", AllowMultipleArgumentsPerToken = true  };
        cmd.Add(idArg); cmd.Add(culturesOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.unpublish", ct); }
            catch (OperationCanceledException) { return 2; }

            var cultures = parseResult.GetValue(culturesOpt);
            var result = await ctx.Client.UnpublishContentAsync(parseResult.GetValue(idArg), cultures?.Length > 0 ? cultures : null, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Content item unpublished.");
            return 0;
        });

        return cmd;
    }
}
