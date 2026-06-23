using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryCreateCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("create", "Create a dictionary item.");
        var keyOpt = new Option<string>("--key") { Required = true };
        // --values accepts en=Hello da=Hej style pairs
        var valuesOpt = new Option<string[]>("--values") { Description = "Translations as lang=value pairs (e.g. --values en=Hello --values da=Hej).", AllowMultipleArgumentsPerToken = true  };
        cmd.Add(keyOpt); cmd.Add(valuesOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "dictionary.create", ct); }
            catch (OperationCanceledException) { return 2; }

            var translations = (parseResult.GetValue(valuesOpt) ?? [])
                .Select(v => v.Split('=', 2))
                .Where(p => p.Length == 2)
                .Select(p => new DictionaryTranslation { IsoCode = p[0], Translation = p[1] });

            var result = await ctx.Client.CreateDictionaryItemAsync(new CreateDictionaryItemRequest { Name = parseResult.GetValue(keyOpt)!, Translations = translations }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
