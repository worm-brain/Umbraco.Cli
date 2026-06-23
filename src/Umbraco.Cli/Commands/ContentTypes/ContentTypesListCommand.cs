using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List all document types defined in the Umbraco instance.\n\nExamples:\n  umbraco content-types list\n  umbraco content-types list --output json | jq '.[].alias'");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content-types.list", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetDocumentTypesAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteTable(["ID", "Name", "Alias", "IsElement"], result.Data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Alias, i.IsElement.ToString() }) ?? []);
            return 0;
        });
        return cmd;
    }
}
