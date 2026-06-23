using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Content;

public static class ContentCreateCommand
{
    public static Command Build(
        Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt,
        CommandContextFactory factory)
    {
        var cmd = new Command("create", "Create a new content item.");
        var typeOpt = new Option<string>("--content-type") { Description = "Document type alias.", Required = true  };
        var nameOpt = new Option<string>("--name") { Description = "Content item name.", Required = true  };
        var parentOpt = new Option<Guid?>("--parent") { Description = "Parent content item ID." };
        var bodyOpt = new Option<FileInfo?>("--json-body") { Description = "Path to JSON file with full request body." };
        cmd.Add(typeOpt); cmd.Add(nameOpt); cmd.Add(parentOpt); cmd.Add(bodyOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "content.create", ct); }
            catch (OperationCanceledException) { return 2; }

            CreateContentRequest request;
            var bodyFile = parseResult.GetValue(bodyOpt);
            if (bodyFile is not null)
            {
                var json = await File.ReadAllTextAsync(bodyFile.FullName, ct);
                request = JsonSerializer.Deserialize<CreateContentRequest>(json)
                    ?? throw new InvalidOperationException("Invalid JSON body.");
            }
            else
            {
                var alias = parseResult.GetValue(typeOpt)!;
                var name = parseResult.GetValue(nameOpt)!;
                var parentId = parseResult.GetValue(parentOpt);
                request = new CreateContentRequest
                {
                    ContentType = new ContentTypeReference { Alias = alias },
                    Parent = parentId.HasValue ? new ContentParentReference { Id = parentId.Value } : null,
                    Variants = [new ContentVariant { Name = name }],
                };
            }

            var result = await ctx.Client.CreateContentAsync(request, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
        });

        return cmd;
    }
}
