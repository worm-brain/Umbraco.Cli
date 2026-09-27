using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Raw access to media items and their files for the media export/diff/apply pipeline (#226,
/// ADR 0008). Mirrors <see cref="IContentSnapshotClient"/>: the verbatim Management API body of
/// each item, its tree placement returned separately (the item read carries no parent), plus the
/// two file operations a promotion needs - download a stored file, and stage one for a write.
/// </summary>
public interface IMediaSnapshotClient
{
    /// <summary>
    /// Enumerates a media subtree in pre-order (a parent always precedes its children), returning
    /// each item's id and parent. With no <paramref name="root"/> it walks the whole media tree;
    /// with one it returns that item as a top-level node (parent null) followed by its descendants.
    /// </summary>
    /// <param name="root">The subtree root, or null for the whole media tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The items' placements in pre-order, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetMediaSnapshotTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    );

    /// <summary>Gets a media item's verbatim <c>GET /media/{id}</c> body.</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetMediaRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a media item from a verbatim body (<c>POST /media</c>, its id preserved).</summary>
    /// <param name="body">The body, carrying its <c>id</c>, <c>parent</c> and <c>mediaType</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateMediaRawAsync(JsonNode body, CancellationToken ct = default);

    /// <summary>Replaces a media item from a verbatim body (<c>PUT /media/{id}</c>).</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="body">The body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateMediaRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>
    /// Stages a file with <c>POST /temporary-file</c>, for a create or update to reference as
    /// <c>{"temporaryFileId": ...}</c>.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="fileName">The file name the item will keep.</param>
    /// <param name="contentType">The file's MIME type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The temporary file id, or a mapped failure.</returns>
    Task<UmbracoResponse<Guid>> StageTemporaryFileAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct = default
    );

    /// <summary>
    /// Downloads a stored media file (the <c>src</c> of an item's <c>umbracoFile</c>) into
    /// <paramref name="destination"/>. Media files are served by the site, not the Management API,
    /// so the path is resolved against the configured host. An absolute URL on another host (a CDN)
    /// is refused rather than fetched, so the access token never leaves the configured host.
    /// </summary>
    /// <param name="src">The host-relative path (<c>/media/...</c>) or an absolute URL on the configured host.</param>
    /// <param name="destination">Where to write the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DownloadMediaFileAsync(
        string src,
        Stream destination,
        CancellationToken ct = default
    );
}
