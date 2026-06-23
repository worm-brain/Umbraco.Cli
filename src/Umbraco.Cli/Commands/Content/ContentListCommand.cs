using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentListCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("list", "List content items.");
        var parentOpt = new Option<Guid?>("--parent") { Description = "Filter by parent ID." };
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0, Description = "Number of items to skip." };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20, Description = "Number of items to return." };
        cmd.Add(parentOpt); cmd.Add(skipOpt); cmd.Add(takeOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.list", ct); }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.GetContentAsync(parseResult.GetValue(parentOpt), parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }

            var items = result.Data?.Items ?? [];
            ctx.Output.WriteTable(
                ["ID", "Name", "Content Type", "Published"],
                items.Select(i => new[] { i.Id.ToString(), i.Name, i.ContentType?.Alias ?? "", i.IsPublished.ToString() }));
            return 0;
        });

        return cmd;
    }
}
