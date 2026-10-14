using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Media;

/// <summary>Wires the <c>media</c> noun: the media library.</summary>
public static class MediaCommand
{
    /// <summary>Builds the <c>media</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "media",
            "Manage Umbraco media library items (upload, list, get, delete)."
        ).WithExamples(
            "umbraco media list",
            "umbraco media upload ./photo.jpg --parent <folder-id>",
            "umbraco media delete <id>"
        );
        cmd.Add(MediaListCommand.Build(executor));
        cmd.Add(MediaTreeCommand.Build(executor));
        cmd.Add(MediaFindCommand.Build(executor));
        cmd.Add(MediaGetCommand.Build(executor));
        cmd.Add(MediaUploadCommand.Build(executor));
        cmd.Add(MediaUpdateCommand.Build(executor));
        cmd.Add(MediaFolderCommand.Build(executor));
        cmd.Add(MediaDeleteCommand.Build(executor));
        cmd.Add(MediaTrashCommand.Build(executor));
        cmd.Add(MediaRestoreCommand.Build(executor));
        cmd.Add(MediaEmptyRecycleBinCommand.Build(executor));
        cmd.Add(MediaMoveCommand.Build(executor));
        cmd.Add(MediaSortCommand.Build(executor));
        // #226: the GUID-preserving promotion pipeline, alongside content's and schema's.
        cmd.Add(MediaExportCommand.Build(executor));
        cmd.Add(MediaDiffCommand.Build(executor));
        cmd.Add(MediaApplyCommand.Build(executor));
        return cmd;
    }
}
