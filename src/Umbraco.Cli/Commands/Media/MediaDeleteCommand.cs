using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Media;

public static class MediaDeleteCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("delete", "Permanently delete a media item by UUID.\n\nExample:\n  umbraco media delete 3f7a8b2e-...");
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "media.delete", ct); }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.DeleteMediaAsync(parseResult.GetValue(idArg), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Media item deleted.");
            return 0;
        });

        return cmd;
    }
}
