using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Imaging;

/// <summary>
/// Wires the read-only <c>imaging</c> noun (issue #121): generate resized image URLs for media items.
/// </summary>
public static class ImagingCommand
{
    /// <summary>Builds the <c>imaging</c> noun with its <c>resize-urls</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "imaging",
            "Generate resized image URLs for media items.\n\nExample:\n  umbraco imaging resize-urls --id <guid> --width 300 --height 200 --mode Crop"
        );
        cmd.Add(BuildResizeUrls(executor));
        return cmd;
    }

    private static Command BuildResizeUrls(CommandExecutor executor)
    {
        var cmd = new Command("resize-urls", "Get resized URLs for one or more media items.");
        var idOpt = new Option<Guid[]>("--id")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = true,
            Description = "Media item ID (repeat for several).",
        };
        var widthOpt = new Option<int?>("--width") { Description = "Target width in pixels." };
        var heightOpt = new Option<int?>("--height") { Description = "Target height in pixels." };
        var modeOpt = new Option<ImageResizeMode?>("--mode")
        {
            Description = "Crop/resize mode: Crop, Max, Stretch, Pad, BoxPad, Min.",
        };
        var formatOpt = new Option<string?>("--format")
        {
            Description = "Output format (e.g. webp, jpg).",
        };
        cmd.Add(idOpt);
        cmd.Add(widthOpt);
        cmd.Add(heightOpt);
        cmd.Add(modeOpt);
        cmd.Add(formatOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "imaging.resize-urls",
                    (client, c) =>
                        client.GetResizeUrlsAsync(
                            parseResult.GetValue(idOpt)!,
                            parseResult.GetValue(widthOpt),
                            parseResult.GetValue(heightOpt),
                            parseResult.GetValue(modeOpt),
                            parseResult.GetValue(formatOpt),
                            c
                        ),
                    new[] { "Id", "Culture", "Url" },
                    data =>
                        (data ?? []).SelectMany(m =>
                            m.Urls.Select(u =>
                                new[] { m.Id.ToString(), u.Culture ?? "", u.Url ?? "" }
                            )
                        ),
                    ct
                )
        );
        return cmd;
    }
}
