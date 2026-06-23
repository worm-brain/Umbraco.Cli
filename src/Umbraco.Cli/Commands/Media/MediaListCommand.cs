using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Media;

public static class MediaListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List media items in the media library.\n\nExamples:\n  umbraco media list\n  umbraco media list --parent <folder-id> --output json");
        var parentOpt = new Option<Guid?>("--parent") { Description = "Filter by parent media folder UUID. Omit for root media items." };
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(parentOpt); cmd.Add(skipOpt); cmd.Add(takeOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "media.list", ct); }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.GetMediaAsync(parseResult.GetValue(parentOpt), parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            var items = result.Data?.Items ?? [];
            ctx.Output.WriteTable(["ID", "Name", "Media Type"], items.Select(i => new[] { i.Id.ToString(), i.Name, i.MediaType?.Alias ?? "" }));
            return 0;
        });

        return cmd;
    }
}
