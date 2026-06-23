using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Members;

public static class MembersListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List members.");
        var groupOpt = new Option<string?>("--group"); var skipOpt = new Option<int>("--skip"); var takeOpt = new Option<int>("--take");
        cmd.Add(groupOpt); cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "members.list", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetMembersAsync(parseResult.GetValue(groupOpt), parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteTable(["ID", "Name", "Email", "Approved"], result.Data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Email, i.IsApproved.ToString() }) ?? []);
            return 0;
        });
        return cmd;
    }
}
