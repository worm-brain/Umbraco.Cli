using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCreateCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("create", "Create a new member.");
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var typeOpt = new Option<string>("--type") { Description = "Member type alias.", Required = true  };
        cmd.Add(emailOpt); cmd.Add(nameOpt); cmd.Add(typeOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "members.create", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.CreateMemberAsync(new CreateMemberRequest
            {
                Email = parseResult.GetValue(emailOpt)!,
                Name = parseResult.GetValue(nameOpt)!,
                MemberType = new ContentTypeReference { Alias = parseResult.GetValue(typeOpt)! },
            }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
