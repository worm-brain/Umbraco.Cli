using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media upload</c> command.</summary>
public static class MediaUploadCommand
{
    /// <summary>
    /// Builds the <c>media upload</c> command. The file is staged via the temporary-file
    /// endpoint and then a media item is created referencing it (issue #57), so large files
    /// upload reliably. <c>--parent</c> is optional now — omit it to upload into the media root.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "upload",
            "Upload a local file as a media item (staged via temporary-file for large files).\n\nExamples:\n  umbraco media upload ./logo.png\n  umbraco media upload ./big-video.mp4 --media-type File --name \"Promo\"\n  umbraco media upload ./photo.jpg --parent 3f7a8b2e-..."
        );
        var fileArg = new Argument<FileInfo>("file") { Description = "Local file to upload." };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "UUID of the media folder to upload into. Omit for the media root.",
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Display name for the media item. Defaults to the filename.",
        };
        var mediaTypeOpt = new Option<string>("--media-type")
        {
            DefaultValueFactory = _ => "Image",
            Description =
                "Media type to create the item as: a media type id (GUID) or name (e.g. Image, File). Defaults to Image.",
        };
        cmd.Add(fileArg);
        cmd.Add(parentOpt);
        cmd.Add(nameOpt);
        cmd.Add(mediaTypeOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "media.upload",
                    async (client, c) =>
                    {
                        var file = parseResult.GetValue(fileArg)!;
                        var name = parseResult.GetValue(nameOpt) ?? file.Name;
                        var mimeType = MimeTypeFor(file.Extension);

                        await using var stream = file.OpenRead();
                        return await client.UploadMediaAsync(
                            parseResult.GetValue(parentOpt),
                            name,
                            stream,
                            file.Name,
                            mimeType,
                            parseResult.GetValue(mediaTypeOpt)!,
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }

    private static string MimeTypeFor(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream",
        };
}
