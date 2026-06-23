using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentGetCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("get", "Get a content item by its UUID, including all property values.\n\nExample:\n  umbraco content get 3f7a8b2e-1234-5678-abcd-ef0123456789");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.get", ct); }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.GetContentByIdAsync(parseResult.GetValue(idArg), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });

        return cmd;
    }
}
