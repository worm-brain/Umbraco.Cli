using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List all data types (property editors) configured in the Umbraco instance.\n\nExample:\n  umbraco data-types list");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "data-types.list", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetDataTypesAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteTable(["ID", "Name", "Editor Alias"], result.Data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.EditorAlias }) ?? []);
            return 0;
        });
        return cmd;
    }
}
