namespace Umbraco.Cli.Client;

/// <summary>Media library read, upload, and delete.</summary>
public interface IMediaClient
{
    Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Uploads a local file as a media item using the Umbraco 14+ two-step flow (issue #57):
    /// stage the bytes to the <c>temporary-file</c> endpoint, then create the media item as
    /// JSON referencing that staged file. Staging first means arbitrarily large files upload
    /// reliably (a single multipart create request would otherwise fail on big files).
    /// </summary>
    /// <param name="parentId">Parent media folder id; null for the media root.</param>
    /// <param name="name">Display name for the new media item.</param>
    /// <param name="fileStream">The file contents to upload.</param>
    /// <param name="fileName">The original file name (used for the staged file part).</param>
    /// <param name="contentType">The file's MIME type.</param>
    /// <param name="mediaType">The media type to create the item as: a media type id (GUID) or a media type name (e.g. "Image").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media item (with its id), or a mapped failure.</returns>
    Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid? parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        string mediaType,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteMediaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Moves a media item to the recycle bin (issue #67). Reversible via <see cref="RestoreMediaAsync"/>.</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> TrashMediaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Restores a media item from the recycle bin (issue #67).</summary>
    /// <param name="id">The trashed media item id.</param>
    /// <param name="parentId">Target parent to restore under; null restores to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RestoreMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    );

    /// <summary>Permanently empties the media recycle bin (issue #67). Irreversible.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> EmptyMediaRecycleBinAsync(CancellationToken ct = default);

    /// <summary>Moves a media item under a new parent folder (issue #67).</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="parentId">Target parent folder id; null moves to the media root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MoveMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    );
}
