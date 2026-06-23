using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesDeleteCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("delete", "Remove a language.");
        var isoArg = new Argument<string>("iso-code") { Description = "ISO culture code (e.g. en-US)." }; cmd.Add(isoArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "languages.delete", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.DeleteLanguageAsync(parseResult.GetValue(isoArg)!, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteMessage("Language removed.");
            return 0;
        });
        return cmd;
    }
}
