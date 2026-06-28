using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List media items in the media library.\n\nExamples:\n  umbraco media list\n  umbraco media list --parent <folder-id> --output json");
        var parentOpt = new Option<Guid?>("--parent") { Description = "Filter by parent media folder UUID. Omit for root media items." };
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(parentOpt); cmd.Add(skipOpt); cmd.Add(takeOpt);

        cmd.SetAction((parseResult, ct) => executor.RunTableAsync(
            parseResult, "media.list",
            (client, c) => client.GetMediaAsync(parseResult.GetValue(parentOpt), parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), c),
            ["ID", "Name", "Media Type"],
            data => (data?.Items ?? []).Select(i => new[] { i.Id.ToString(), i.Name, i.MediaType?.Alias ?? "" }),
            ct));

        return cmd;
    }
}
