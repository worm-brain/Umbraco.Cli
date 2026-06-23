using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCreateCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("create", "Add a language.");
        var cultureOpt = new Option<string>("--culture") { Description = "ISO 4646 culture code for the language (e.g. en-US, fr-FR, da-DK).", Required = true  };
        var defaultOpt = new Option<bool>("--default") { DefaultValueFactory = _ => false, Description = "Set this language as the default for new content." };
        var mandatoryOpt = new Option<bool>("--mandatory") { DefaultValueFactory = _ => false, Description = "Mark this language as mandatory (content must be translated into it)." };
        cmd.Add(cultureOpt); cmd.Add(defaultOpt); cmd.Add(mandatoryOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "languages.create", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.CreateLanguageAsync(new CreateLanguageRequest { IsoCode = parseResult.GetValue(cultureOpt)!, IsDefault = parseResult.GetValue(defaultOpt), IsMandatory = parseResult.GetValue(mandatoryOpt) }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
