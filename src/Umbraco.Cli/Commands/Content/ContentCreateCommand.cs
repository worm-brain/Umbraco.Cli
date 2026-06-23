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
        var cmd = new Command("create", "Create a new content item. Supply --json-body for full property control.\n\nExamples:\n  umbraco content create --content-type textPage --name \"About\"\n  umbraco content create --content-type textPage --name \"Child\" --parent <id>\n  umbraco content create --content-type blogPost --name \"Post\" --json-body ./body.json");
        var typeOpt = new Option<string>("--content-type") { Description = "Alias of the document type to create (e.g. textPage, blogPost).", Required = true  };
        var nameOpt = new Option<string>("--name") { Description = "Display name for the new content item.", Required = true  };
        var parentOpt = new Option<Guid?>("--parent") { Description = "Parent content item UUID. Omit to create at the root." };
        var bodyOpt = new Option<FileInfo?>("--json-body") { Description = "Path to a JSON file containing the full create request body (overrides other flags)." };
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
