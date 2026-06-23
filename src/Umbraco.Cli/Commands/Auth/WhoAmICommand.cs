using System.CommandLine;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class WhoAmICommand
{
    public static Command Build(
        Option<string?> hostOption,
        Option<string?> tokenOption,
        Option<string?> outputOption,
        CommandContextFactory factory)
    {
        var cmd = new Command("whoami", "Show the currently authenticated user.");

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try
            {
                ctx = await factory.CreateAsync(
                    parseResult.GetValue(hostOption),
                    parseResult.GetValue(tokenOption),
                    LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOption)),
                    "auth.whoami", ct);
            }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.GetCurrentUserAsync(ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });

        return cmd;
    }
}
