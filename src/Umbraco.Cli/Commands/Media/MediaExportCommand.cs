using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>The summary <c>media export</c> reports: what was written, and where.</summary>
/// <param name="Items">Number of media items exported (folders included).</param>
/// <param name="Files">Number of files downloaded.</param>
/// <param name="Bytes">Total size of the files.</param>
/// <param name="UnavailableFiles">
/// The files the site would not serve (404 gone, 403 protected). Their items are exported without
/// them: apply creates such an item without a file, and leaves an existing one's file alone.
/// Empty when every file was downloaded.
/// </param>
/// <param name="Path">The absolute path of the snapshot directory.</param>
public sealed record MediaExportSummary(
    int Items,
    int Files,
    long Bytes,
    IReadOnlyList<UnavailableFile> UnavailableFiles,
    string Path
);

/// <summary>Wires the <c>media export</c> command (#226, ADR 0008).</summary>
public static class MediaExportCommand
{
    /// <summary>
    /// Builds <c>media export</c>: writes a media subtree - every item's body and placement, and
    /// every file - to a snapshot directory. The directory is required (the files are binary, so
    /// the snapshot cannot go to stdout).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "export",
            "Export media items and their files to a snapshot directory.\n\n"
                + "The directory gets media.json (every item's body and placement, by id) and "
                + "files/<id>/<name>. It must be new, empty, or an earlier media export, which is "
                + "replaced."
        ).WithExamples(
            "umbraco media export --out ./media-snapshot",
            "umbraco media export --root 3f7a8b2e-... -O ./blog-images"
        );
        var rootOpt = new Option<Guid?>("--root")
        {
            Description = "Export only this item's subtree (root included). Omit for all media.",
        };
        var outOpt = new Option<DirectoryInfo>("--out", new[] { "-O" })
        {
            Description = "The snapshot directory to write.",
        }.AsRequired();
        cmd.Add(rootOpt);
        cmd.Add(outOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunAsync(
                    parseResult,
                    (client, c) =>
                        MediaExporter.ExportAsync(
                            client,
                            parseResult.GetValue(rootOpt),
                            parseResult.GetValue(outOpt)!.FullName,
                            c
                        ),
                    (ctx, snapshot) =>
                    {
                        var files = snapshot!
                            .Items.Select(i => i.File)
                            .OfType<MediaFile>()
                            .ToList();
                        ctx.Output.WriteSuccess(
                            new MediaExportSummary(
                                snapshot.Items.Count,
                                files.Count,
                                files.Sum(f => f.Bytes),
                                [
                                    .. snapshot
                                        .Items.Select(i => i.FileUnavailable)
                                        .OfType<UnavailableFile>(),
                                ],
                                snapshot.Directory
                            ),
                            ctx.CommandName,
                            ctx.Stopwatch.ElapsedMilliseconds
                        );
                    },
                    ct
                )
        );
        return cmd;
    }
}
