using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUpdateCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("update", "Update an existing content item by replacing its values with those from a JSON body file.\n\nExample:\n  umbraco content update 3f7a8b2e-... --json-body ./update.json");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var bodyOpt = new Option<FileInfo>("--json-body") { Description = "Path to a JSON file containing the update request body.", Required = true  };
        cmd.Add(idArg); cmd.Add(bodyOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.update", ct); }
            catch (OperationCanceledException) { return 2; }

            var json = await File.ReadAllTextAsync(parseResult.GetValue(bodyOpt)!.FullName, ct);
            var request = JsonSerializer.Deserialize<UpdateContentRequest>(json)
                ?? throw new InvalidOperationException("Invalid JSON body.");

            var result = await ctx.Client.UpdateContentAsync(parseResult.GetValue(idArg), request, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });

        return cmd;
    }
}
