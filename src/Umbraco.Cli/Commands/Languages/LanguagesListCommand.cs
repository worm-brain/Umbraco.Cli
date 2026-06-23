using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesListCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("list", "List configured languages.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "languages.list", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.GetLanguagesAsync(ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteTable(["ISO Code", "Name", "Default", "Mandatory"], result.Data?.Select(l => new[] { l.IsoCode, l.Name, l.IsDefault.ToString(), l.IsMandatory.ToString() }) ?? []);
            return 0;
        });
        return cmd;
    }
}
