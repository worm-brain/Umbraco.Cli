using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List templates.");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "templates.list", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetTemplatesAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteTable(["ID", "Name", "Alias"], result.Data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Alias }) ?? []);
            return 0;
        });
        return cmd;
    }
}
