using System.CommandLine;
using Umbraco.Cli.Commands.Auth;

namespace Umbraco.Cli.Commands.Media;

public static class MediaUploadCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("upload", "Upload a file as a media item.");
        var fileArg = new Argument<FileInfo>("file") { Description = "Local file to upload." };
        var parentOpt = new Option<Guid>("--parent") { Description = "UUID of the media folder to upload into.", Required = true  };
        var nameOpt = new Option<string?>("--name") { Description = "Display name for the media item. Defaults to the filename." };
        cmd.Add(fileArg); cmd.Add(parentOpt); cmd.Add(nameOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            CommandContext ctx;
            try { ctx = await factory.CreateAsync(parseResult.GetValue(hostOpt), parseResult.GetValue(tokenOpt), LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOpt)), "media.upload", ct); }
            catch (OperationCanceledException) { return 2; }

            var file = parseResult.GetValue(fileArg)!;
            var name = parseResult.GetValue(nameOpt) ?? file.Name;
            var mimeType = MimeTypeFor(file.Extension);

            await using var stream = file.OpenRead();
            var result = await ctx.Client.UploadMediaAsync(parseResult.GetValue(parentOpt), name, stream, file.Name, mimeType, ct);
            if (!result.IsSuccess) { ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!); return 1; }
            ctx.Output.WriteSuccess(result.Data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
            return 0;
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
