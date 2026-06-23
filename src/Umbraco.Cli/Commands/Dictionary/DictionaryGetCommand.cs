using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryGetCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("get", "Get a dictionary item and its translations by key.\n\nExample:\n  umbraco dictionary get Common.Search");
        var keyArg = new Argument<string>("key"); cmd.Add(keyArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "dictionary.get", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetDictionaryItemByKeyAsync(parseResult.GetValue(keyArg)!, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
