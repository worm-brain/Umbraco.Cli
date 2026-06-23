using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUnpublishCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("unpublish", "Unpublish a content item.");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = new Option<string[]>("--cultures") { Description = "Cultures to unpublish.", AllowMultipleArgumentsPerToken = true  };
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
