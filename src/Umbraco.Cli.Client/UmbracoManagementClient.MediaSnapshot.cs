using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The media snapshot's raw reads and writes (#226, ADR 0008): the media tree in pre-order, the
/// verbatim item body, and the file download and staging a GUID-preserving promotion needs.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetMediaSnapshotTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<ContentTreeNode>>(
            ct,
            async () =>
            {
                // With an explicit root, include it as a top-level node (parent null) so the
                // exported subtree stands alone, as the content snapshot does.
                if (root is null)
                    return await WalkMediaSnapshotTreeAsync(parent: null, ct);

                var nodes = new List<ContentTreeNode> { new(root.Value, Parent: null) };
                nodes.AddRange(await WalkMediaSnapshotTreeAsync(root, ct));
                return nodes;
            }
        );

    /// <summary>
    /// Recursively enumerates the media tree beneath <paramref name="parent"/> in pre-order,
    /// recording each item's id and parent. The recycle bin is its own tree, so trashed items are
    /// never returned.
    /// </summary>
    /// <param name="parent">The parent whose children to list, or null for the media root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item beneath <paramref name="parent"/>, pre-order.</returns>
    private async Task<List<ContentTreeNode>> WalkMediaSnapshotTreeAsync(
        Guid? parent,
        CancellationToken ct
    )
    {
        var nodes = new List<ContentTreeNode>();
        var skip = 0;
        while (true)
        {
            var (items, total) = await FetchMediaTreePageAsync(parent, skip, TreePageSize, ct);
            foreach (var item in items)
            {
                var id = item.Id ?? Guid.Empty;
                // Pre-order: emit the node before descending, so parents precede their children.
                nodes.Add(new ContentTreeNode(id, parent));
                if (item.HasChildren ?? false)
                    nodes.AddRange(await WalkMediaSnapshotTreeAsync(id, ct));
            }

            skip += items.Count;
            if (items.Count == 0 || skip >= total)
                break;
        }
        return nodes;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetMediaRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => GetRawJsonAsync($"{ApiRoot}/media/{id}", ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateMediaRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => SendRawJsonAsync(Method.POST, $"{ApiRoot}/media", body, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateMediaRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => SendRawJsonAsync(Method.PUT, $"{ApiRoot}/media/{id}", body, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Guid>> StageTemporaryFileAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StageFileAsync(content, fileName, contentType, ct));

    /// <summary>
    /// Posts a file to the temporary-file endpoint (multipart: a client-generated <c>Id</c> part
    /// and the <c>File</c> part) and returns that id. Unguarded; callers wrap it.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="contentType">The file's MIME type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The temporary file id.</returns>
    private async Task<Guid> StageFileAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct
    )
    {
        // The Kiota MultipartBody needs the request adapter to resolve the per-part serializers.
        var temporaryFileId = Guid.NewGuid();
        var multipart = new MultipartBody { RequestAdapter = _adapter };
        multipart.AddOrReplacePart("Id", "text/plain", temporaryFileId.ToString());
        multipart.AddOrReplacePart("File", contentType, content, fileName);
        await _api.Umbraco.Management.Api.V1.TemporaryFile.PostAsync(
            multipart,
            cancellationToken: ct
        );
        return temporaryFileId;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DownloadMediaFileAsync(
        string src,
        Stream destination,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var request = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    URI = MediaFileUri(src),
                };
                // Disposed so each download releases its connection; an export can make hundreds.
                await using var stream =
                    await _adapter.SendPrimitiveAsync<Stream>(request, RawErrorMapping, ct)
                    ?? throw new ApiException($"The media file '{src}' was empty.")
                    {
                        ResponseStatusCode = 204,
                    };
                await stream.CopyToAsync(destination, ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// The URI to download a media file from: a host-relative path resolves against the configured
    /// host; an absolute http(s) URL is used only when it is on that host.
    /// </summary>
    /// <param name="src">The file's <c>src</c>.</param>
    /// <returns>The absolute URI.</returns>
    /// <exception cref="ApiException">The URL is on another host (400).</exception>
    private Uri MediaFileUri(string src)
    {
        // "/media/x" parses as an absolute file: URI on Windows, so only http(s) counts as absolute.
        if (
            Uri.TryCreate(src, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https"
        )
        {
            var host = new Uri(_adapter.BaseUrl!);
            if (
                Uri.Compare(
                    absolute,
                    host,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase
                ) != 0
            )
                throw BadRequest(
                    $"The media file '{src}' is served from another host. The CLI only downloads "
                        + "from the configured host, so the access token is not sent elsewhere."
                );
            return absolute;
        }
        return RawUri(src.TrimStart('/'));
    }
}
