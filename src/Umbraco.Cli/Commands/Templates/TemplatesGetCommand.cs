using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesGetCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("get", "Get a template by its alias, including the view file content.\n\nExample:\n  umbraco templates get master\n  umbraco templates get textPage");
        var aliasArg = new Argument<string>("alias"); cmd.Add(aliasArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "templates.get", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetTemplateByAliasAsync(parseResult.GetValue(aliasArg)!, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
