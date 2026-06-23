using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("media", "Manage Umbraco media items.");
        cmd.Add(MediaListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaUploadCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(MediaDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
