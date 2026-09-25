using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
            "Upload a local file as a media item (staged via temporary-file for large files).\n\nExamples:\n  umbraco media upload ./logo.png\n  umbraco media upload ./big-video.mp4 --media-type File --name \"Promo\"\n  umbraco media upload ./photo.jpg --parent 3f7a8b2e-...\n  umbraco media upload ./report.pdf --media-type brochure --id 3f7a8b2e-... --value title=\"Annual report\""
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
                "Media type to create the item as: a media type id, alias or name (e.g. image, Image, File). Defaults to Image.",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description =
                "UUID to create the item with, so it keeps the same id on every instance (content "
                + "references media by id). Omit to generate one.",
        };
        var valueOpt = new Option<string[]>("--value")
        {
            Description =
                "Property values as alias=value, e.g. title=\"Annual report\". Repeatable. Needed "
                + "for a media type with required fields.",
            AllowMultipleArgumentsPerToken = true,
        };
        cmd.Add(fileArg);
        cmd.Add(parentOpt);
        cmd.Add(nameOpt);
        cmd.Add(mediaTypeOpt);
        cmd.Add(idOpt);
        cmd.Add(valueOpt);
        KeyValuePairs.Validate(cmd, valueOpt, "--value must be alias=value, e.g. title=Brochure");
        // The file itself is umbracoFile; a second value for it would replace the upload.
        cmd.Validators.Add(result =>
        {
            if (
                KeyValuePairs
                    .Parse(result.GetValue(valueOpt))
                    .Any(p =>
                        string.Equals(p.Key, "umbracoFile", StringComparison.OrdinalIgnoreCase)
                    )
            )
                result.AddError("--value cannot set umbracoFile: that is the uploaded file.");
        });

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
                            parseResult.GetValue(idOpt),
                            [
                                .. KeyValuePairs
                                    .Parse(parseResult.GetValue(valueOpt))
                                    .Select(p => new MediaValue { Alias = p.Key, Value = p.Value }),
                            ],
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
