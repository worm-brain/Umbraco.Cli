using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List media items in the media library.\n\nExamples:\n  umbraco media list\n  umbraco media list --parent <folder-id> --output json"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent media folder id; lists root media items if omitted.",
        };
        cmd.Add(parentOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetMediaAsync(parseResult.GetValue(parentOpt), skip, take, c),
                    // Media Type is intentionally omitted: the media-tree list items carry only
                    // the type id (no alias), so the column was always blank (#75). Use
                    // 'media get <id>' for the full media type.
                    ["ID", "Name"],
                    i => new[] { i.Id.ToString(), i.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
