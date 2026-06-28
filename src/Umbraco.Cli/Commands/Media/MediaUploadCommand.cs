using System.CommandLine;

namespace Umbraco.Cli.Commands.Media;

public static class MediaUploadCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("upload", "Upload a file as a media item.");
        var fileArg = new Argument<FileInfo>("file") { Description = "Local file to upload." };
        var parentOpt = new Option<Guid>("--parent") { Description = "UUID of the media folder to upload into.", Required = true  };
        var nameOpt = new Option<string?>("--name") { Description = "Display name for the media item. Defaults to the filename." };
        cmd.Add(fileArg); cmd.Add(parentOpt); cmd.Add(nameOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var name = parseResult.GetValue(nameOpt) ?? file.Name;
            var mimeType = MimeTypeFor(file.Extension);

            await using var stream = file.OpenRead();
            return await executor.RunObjectAsync(
                parseResult, "media.upload",
                (client, c) => client.UploadMediaAsync(parseResult.GetValue(parentOpt), name, stream, file.Name, mimeType, c),
                ct);
        });

        return cmd;
    }

    private static string MimeTypeFor(string ext) => ext.ToLowerInvariant() switch
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
