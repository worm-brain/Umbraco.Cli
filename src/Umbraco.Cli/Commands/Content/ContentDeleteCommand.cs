using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentDeleteCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("delete", "Permanently delete a content item by ID. This cannot be undone.\n\nExample:\n  umbraco content delete 3f7a8b2e-1234-5678-abcd-ef0123456789");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.delete", ct); }
            catch (OperationCanceledException) { return 2; }

            var result = await ctx.Client.DeleteContentAsync(parseResult.GetValue(idArg), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Content item deleted.");
            return 0;
        });

        return cmd;
    }
}
