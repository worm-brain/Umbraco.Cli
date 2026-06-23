using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Users;

public static class UsersInviteCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("invite", "Invite a new backoffice user.");
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var msgOpt = new Option<string?>("--message");
        cmd.Add(emailOpt); cmd.Add(nameOpt); cmd.Add(msgOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "users.invite", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.InviteUserAsync(new InviteUserRequest { Email = parseResult.GetValue(emailOpt)!, Name = parseResult.GetValue(nameOpt)!, Message = parseResult.GetValue(msgOpt) }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage($"Invitation sent to {parseResult.GetValue(emailOpt)}.");
            return 0;
        });
        return cmd;
    }
}
