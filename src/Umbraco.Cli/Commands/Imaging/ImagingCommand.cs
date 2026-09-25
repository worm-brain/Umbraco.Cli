using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
            "Generate resized image URLs for media items.\n\nExample:\n  umbraco imaging resize-urls <id> --width 300 --height 200 --mode Crop"
        );
        cmd.Add(BuildResizeUrls(executor));
        return cmd;
    }

    private static Command BuildResizeUrls(CommandExecutor executor)
    {
        var cmd = new Command(
            "resize-urls",
            "Get resized URLs for one or more media items.\n\nExamples:\n  umbraco imaging resize-urls <id> --width 300\n  umbraco imaging resize-urls <id> <id> --width 300 --height 200 --mode Crop --format webp"
        );
        // Several known targets are a variadic positional (docs/conventions.md 3.3).
        var idOpt = new Argument<Guid[]>("id")
        {
            Description = "The media items to resize: one or more ids.",
            Arity = ArgumentArity.OneOrMore,
        };
        var widthOpt = new Option<int?>("--width") { Description = "Target width in pixels." };
        var heightOpt = new Option<int?>("--height") { Description = "Target height in pixels." };
        var modeOpt = new Option<ImageResizeMode?>("--mode")
        {
            Description = "Crop/resize mode: Crop, Max, Stretch, Pad, BoxPad, Min.",
        };
        var formatOpt = new Option<string?>("--format")
        {
            Description =
                "Output format as a file extension (e.g. webp or .webp, jpg). The leading dot is added if missing.",
        };
        cmd.Add(idOpt);
        cmd.Add(widthOpt);
        cmd.Add(heightOpt);
        cmd.Add(modeOpt);
        cmd.Add(formatOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
                    (client, c) =>
                        client.GetResizeUrlsAsync(
                            parseResult.GetValue(idOpt)!,
                            parseResult.GetValue(widthOpt),
                            parseResult.GetValue(heightOpt),
                            parseResult.GetValue(modeOpt),
                            parseResult.GetValue(formatOpt),
                            c
                        ),
                    // One row per media item rather than per URL: structured output now carries
                    // the nested { id, urls[] } shape from the DTO, which is truer than the
                    // flattened rows it used to emit, so the table joins them for reading.
                    new[] { "Id", "Urls" },
                    m =>
                        new[]
                        {
                            m.Id.ToString(),
                            string.Join(", ", m.Urls.Select(u => u.Url ?? "")),
                        },
                    ct
                )
        );
        return cmd;
    }
}
