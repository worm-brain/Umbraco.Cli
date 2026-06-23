using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesGetCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("get", "Get a document type by UUID or alias, including its property groups.\n\nExamples:\n  umbraco content-types get textPage\n  umbraco content-types get 3f7a8b2e-...");
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content-types.get", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetDocumentTypeByIdAsync(parseResult.GetValue(idArg), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
