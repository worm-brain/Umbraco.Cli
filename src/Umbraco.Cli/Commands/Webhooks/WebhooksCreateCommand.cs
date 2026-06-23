using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksCreateCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("create", "Create a webhook.");
        var urlOpt = new Option<string>("--url") { Required = true };
        var eventsOpt = new Option<string[]>("--events") { Description = "Event names to subscribe to. Repeat --events for multiple: --events ContentPublished --events MediaSaved.", Required = true, AllowMultipleArgumentsPerToken = true  };
        cmd.Add(urlOpt); cmd.Add(eventsOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "webhooks.create", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.CreateWebhookAsync(new CreateWebhookRequest { Url = parseResult.GetValue(urlOpt)!, Events = parseResult.GetValue(eventsOpt) ?? [] }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
