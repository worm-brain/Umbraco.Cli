using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCreateCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("create", "Create a new document type.");
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var descOpt = new Option<string?>("--description");
        var isElementOpt = new Option<bool>("--is-element") { DefaultValueFactory = _ => false };
        var allowRootOpt = new Option<bool>("--allow-at-root") { DefaultValueFactory = _ => false };
        cmd.Add(nameOpt); cmd.Add(aliasOpt); cmd.Add(descOpt); cmd.Add(isElementOpt); cmd.Add(allowRootOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content-types.create", ct); }
            catch (OperationCanceledException) { return 2; }
            var result = await ctx.Client.CreateDocumentTypeAsync(new CreateDocumentTypeRequest
            {
                Name = parseResult.GetValue(nameOpt)!,
                Alias = parseResult.GetValue(aliasOpt)!,
                Description = parseResult.GetValue(descOpt),
                IsElement = parseResult.GetValue(isElementOpt),
                AllowedAsRoot = parseResult.GetValue(allowRootOpt),
            }, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });
        return cmd;
    }
}
