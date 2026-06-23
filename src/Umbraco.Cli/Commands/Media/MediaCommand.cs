using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("media", "Manage Umbraco media library items (upload, list, get, delete).\n\nExamples:\n  umbraco media list\n  umbraco media upload ./photo.jpg --parent <folder-id>\n  umbraco media delete <id>");
        cmd.Add(MediaListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaUploadCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
