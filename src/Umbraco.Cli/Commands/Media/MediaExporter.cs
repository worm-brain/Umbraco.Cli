using System.Security.Cryptography;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// Reads media from a live instance for the media pipeline (#226, ADR 0008): the whole snapshot
/// with its files for <c>media export</c>, or just the items and what is known of their files for
/// <c>diff</c>/<c>apply</c>, which only download a live file when asked to verify it.
/// <para>
/// Both fail fast: a failed tree walk, item read or download returns that failure rather than a
/// partial result, which would diff and apply as if the unread items had been deleted.
/// </para>
/// </summary>
public static class MediaExporter
{
    /// <summary>
    /// Exports the media subtree beneath <paramref name="root"/> (the whole tree when null) into
    /// <paramref name="directory"/>: each file under <c>files/{id}/</c>, then the index. The index is
    /// written last and any earlier one removed first, so a failed export never leaves an index
    /// describing files that are not there.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="root">The subtree root, or null for the whole media tree.</param>
    /// <param name="directory">The snapshot directory; created if missing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot written, or the first failure.</returns>
    /// <exception cref="InvalidInputException">The directory holds other files and no earlier snapshot.</exception>
    public static async Task<UmbracoResponse<MediaSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        Guid? root,
        string directory,
        CancellationToken ct
    )
    {
        var full = Path.GetFullPath(directory);
        PrepareDirectory(full);

        var live = await ReadAsync(client, root, ct);
        if (!live.IsSuccess)
            return UmbracoResponse<MediaSnapshot>.Failure(live.StatusCode, live.ErrorMessage!);

        var items = new List<MediaNode>(live.Data!.Count);
        foreach (var node in live.Data!)
        {
            if (MediaBody.SrcOf(node.Body) is not { } src)
            {
                items.Add(
                    new MediaNode
                    {
                        Id = node.Id,
                        Parent = node.Parent,
                        Body = node.Body,
                    }
                );
                continue;
            }

            // The name is kept as the item has it (diff compares it with the live one, and apply
            // uploads under it); only the copy on disk needs a name every OS accepts.
            var name = MediaBody.FileNameOf(src);
            var onDisk = SafeFileName(name);
            var relative = $"{MediaSnapshot.FilesDirectoryName}/{node.Id}/{onDisk}";
            var path = Path.Combine(
                full,
                MediaSnapshot.FilesDirectoryName,
                node.Id.ToString(),
                onDisk
            );
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using (var output = File.Create(path))
            {
                var download = await client.DownloadMediaFileAsync(src, output, ct);
                if (!download.IsSuccess)
                    return UmbracoResponse<MediaSnapshot>.Failure(
                        download.StatusCode,
                        $"Could not download the file of media item {node.Id} ({src}): "
                            + download.ErrorMessage
                    );
            }

            items.Add(
                new MediaNode
                {
                    Id = node.Id,
                    Parent = node.Parent,
                    Body = node.Body,
                    File = new MediaFile
                    {
                        Path = relative,
                        Name = name,
                        Bytes = new FileInfo(path).Length,
                        Sha256 = await HashFileAsync(path, ct),
                    },
                }
            );
        }

        var snapshot = new MediaSnapshot
        {
            Root = root,
            Items = items,
            Directory = full,
        };
        await File.WriteAllTextAsync(
            Path.Combine(full, MediaSnapshot.IndexFileName),
            snapshot.ToJson(),
            ct
        );
        return UmbracoResponse<MediaSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Reads the live items beneath <paramref name="root"/> with what their bodies say about their
    /// files: the name (from <c>src</c>) and the size (<c>umbracoBytes</c>, -1 when absent). No
    /// file is downloaded; see <see cref="HashLiveFileAsync"/>.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="root">The subtree root, or null for the whole media tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The live items in pre-order, or the first failure.</returns>
    public static async Task<UmbracoResponse<List<MediaNode>>> ReadLiveAsync(
        IUmbracoManagementClient client,
        Guid? root,
        CancellationToken ct
    )
    {
        var live = await ReadAsync(client, root, ct);
        if (!live.IsSuccess)
            return live;
        return UmbracoResponse<List<MediaNode>>.Success([
            .. live.Data!.Select(n => new MediaNode
            {
                Id = n.Id,
                Parent = n.Parent,
                Body = n.Body,
                File = MediaBody.SrcOf(n.Body) is { } src
                    ? new MediaFile
                    {
                        Name = MediaBody.FileNameOf(src),
                        Bytes = MediaBody.BytesOf(n.Body) ?? -1,
                    }
                    : null,
            }),
        ]);
    }

    /// <summary>
    /// Downloads a live item's file and returns its SHA-256, streaming it through the hash rather
    /// than holding it in memory (<c>--verify-files</c>).
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="src">The file's <c>src</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lower-case hex hash, or the download failure.</returns>
    public static async Task<UmbracoResponse<string>> HashLiveFileAsync(
        IUmbracoManagementClient client,
        string src,
        CancellationToken ct
    )
    {
        using var sha = SHA256.Create();
        await using var hashing = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        var download = await client.DownloadMediaFileAsync(src, hashing, ct);
        if (!download.IsSuccess)
            return UmbracoResponse<string>.Failure(download.StatusCode, download.ErrorMessage!);
        await hashing.FlushFinalBlockAsync(ct);
        return UmbracoResponse<string>.Success(Convert.ToHexStringLower(sha.Hash!));
    }

    /// <summary>The SHA-256 of a file on disk, lower-case hex.</summary>
    /// <param name="path">The file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hash.</returns>
    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    /// <summary>Walks the tree and reads every item's verbatim body, in pre-order.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="root">The subtree root, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The items (file unset), or the first failure.</returns>
    private static async Task<UmbracoResponse<List<MediaNode>>> ReadAsync(
        IUmbracoManagementClient client,
        Guid? root,
        CancellationToken ct
    )
    {
        var tree = await client.GetMediaSnapshotTreeAsync(root, ct);
        if (!tree.IsSuccess)
            return UmbracoResponse<List<MediaNode>>.Failure(tree.StatusCode, tree.ErrorMessage!);

        var nodes = new List<MediaNode>(tree.Data!.Count);
        foreach (var node in tree.Data!)
        {
            var raw = await client.GetMediaRawAsync(node.Id, ct);
            if (!raw.IsSuccess)
                return UmbracoResponse<List<MediaNode>>.Failure(raw.StatusCode, raw.ErrorMessage!);
            nodes.Add(
                new MediaNode
                {
                    Id = node.Id,
                    Parent = node.Parent,
                    Body = raw.Data!,
                }
            );
        }
        return UmbracoResponse<List<MediaNode>>.Success(nodes);
    }

    /// <summary>
    /// Makes <paramref name="directory"/> ready for an export: created when missing; when it holds
    /// an earlier snapshot, that snapshot's index and files are removed; any other non-empty
    /// directory is refused rather than written into.
    /// </summary>
    /// <param name="directory">The full path.</param>
    /// <exception cref="InvalidInputException">The directory holds files that are not a snapshot.</exception>
    private static void PrepareDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return;
        }

        var index = Path.Combine(directory, MediaSnapshot.IndexFileName);
        if (!File.Exists(index))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidInputException(
                    $"'{directory}' is not empty and holds no {MediaSnapshot.IndexFileName}. "
                        + "Export into an empty or new directory, or over an earlier media export."
                );
            return;
        }

        // An earlier export: replace it whole, so no file from it lingers.
        File.Delete(index);
        var files = Path.Combine(directory, MediaSnapshot.FilesDirectoryName);
        if (Directory.Exists(files))
            Directory.Delete(files, recursive: true);
    }

    /// <summary>A file name safe to write on any OS; <c>file</c> when nothing usable is left.</summary>
    /// <param name="name">The name from the item's <c>src</c>.</param>
    /// <returns>The sanitised name.</returns>
    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var safe = new string([
            .. Path.GetFileName(name).Select(c => invalid.Contains(c) ? '_' : c),
        ]);
        return safe is "" or "." or ".." ? "file" : safe;
    }
}
