using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Members;

public static class MembersDeleteCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("delete", "Permanently delete a member by UUID.\n\nExample:\n  umbraco members delete 3f7a8b2e-...");
        var idArg = new Argument<Guid>("id"); cmd.Add(idArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "members.delete", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.DeleteMemberAsync(parseResult.GetValue(idArg), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Member deleted.");
            return 0;
        });
        return cmd;
    }
}
