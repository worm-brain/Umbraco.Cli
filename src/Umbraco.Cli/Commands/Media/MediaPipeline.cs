using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// The shared prelude for <c>media diff</c> and <c>media apply</c> (#226): load the snapshot, read
/// the live media at the snapshot's own root (so a subtree's prune stays inside it), optionally
/// hash the live files the snapshot also has, and compute the diff.
/// </summary>
public static class MediaPipeline
{
    /// <summary>Loads the snapshot, reads the live side, and diffs them.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="snapshotPath">The snapshot directory, or the path of its index.</param>
    /// <param name="verifyFiles">
    /// Download each live file the snapshot also holds and compare hashes. Without it, a file is
    /// compared by name and size only, which needs no download.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot and the diff, or the first failure.</returns>
    public static async Task<
        UmbracoResponse<(MediaSnapshot Snapshot, MediaDiff Diff)>
    > DiffAgainstLiveAsync(
        IUmbracoManagementClient client,
        string snapshotPath,
        bool verifyFiles,
        CancellationToken ct
    )
    {
        var desired = await MediaSnapshot.LoadAsync(snapshotPath, ct);
        var live = await MediaExporter.ReadLiveAsync(client, desired.Root, ct);
        if (!live.IsSuccess)
            return UmbracoResponse<(MediaSnapshot, MediaDiff)>.Failure(
                live.StatusCode,
                live.ErrorMessage!
            );

        var nodes = live.Data!;
        if (verifyFiles)
        {
            var withFiles = desired
                .Items.Where(i => i.File is not null)
                .Select(i => i.Id)
                .ToHashSet();
            for (var i = 0; i < nodes.Count; i++)
            {
                if (
                    !withFiles.Contains(nodes[i].Id)
                    || MediaBody.SrcOf(nodes[i].Body) is not { } src
                )
                    continue;
                var hash = await MediaExporter.HashLiveFileAsync(client, src, ct);
                if (!hash.IsSuccess)
                    return UmbracoResponse<(MediaSnapshot, MediaDiff)>.Failure(
                        hash.StatusCode,
                        $"Could not download the file of media item {nodes[i].Id} ({src}): "
                            + hash.ErrorMessage
                    );
                nodes[i] = new MediaNode
                {
                    Id = nodes[i].Id,
                    Parent = nodes[i].Parent,
                    Body = nodes[i].Body,
                    File = new MediaFile
                    {
                        Name = nodes[i].File!.Name,
                        Bytes = nodes[i].File!.Bytes,
                        Sha256 = hash.Data!,
                    },
                };
            }
        }

        return UmbracoResponse<(MediaSnapshot, MediaDiff)>.Success(
            (desired, MediaDiffEngine.Compare(desired, nodes))
        );
    }
}
