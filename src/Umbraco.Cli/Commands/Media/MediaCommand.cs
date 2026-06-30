using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("media", "Manage Umbraco media library items (upload, list, get, delete).\n\nExamples:\n  umbraco media list\n  umbraco media upload ./photo.jpg --parent <folder-id>\n  umbraco media delete <id>");
        cmd.Add(MediaListCommand.Build(executor));
        cmd.Add(MediaGetCommand.Build(executor));
        cmd.Add(MediaUploadCommand.Build(executor));
        cmd.Add(MediaDeleteCommand.Build(executor));
        return cmd;
    }
}
