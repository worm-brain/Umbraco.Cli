using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

public static class ContentUpdateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("update", "Update an existing content item by replacing its values with those from a JSON body file.\n\nExample:\n  umbraco content update 3f7a8b2e-... --json-body ./update.json");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var bodyOpt = new Option<FileInfo>("--json-body") { Description = "Path to a JSON file containing the update request body.", Required = true  };
        cmd.Add(idArg); cmd.Add(bodyOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = await File.ReadAllTextAsync(parseResult.GetValue(bodyOpt)!.FullName, ct);
            var request = JsonSerializer.Deserialize<UpdateContentRequest>(json)
                ?? throw new InvalidOperationException("Invalid JSON body.");

            return await executor.RunObjectAsync(
                parseResult, "content.update",
                (client, c) => client.UpdateContentAsync(parseResult.GetValue(idArg), request, c),
                ct);
        });

        return cmd;
    }
}
