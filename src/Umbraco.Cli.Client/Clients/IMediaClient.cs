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
    /// <param name="id">The id to create the item with, so it keeps its GUID across instances (#226); null generates one.</param>
    /// <param name="values">Property values to set besides the file (#220); must not include <c>umbracoFile</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media item (with its id), or a mapped failure.</returns>
    Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid? parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        string mediaType,
        Guid? id = null,
        IReadOnlyList<MediaValue>? values = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a media folder (#171).
    /// <para>
    /// There is no folder endpoint for media - unlike data types and blueprints, which have real
    /// ones. A media folder is an ordinary media item of the <c>Folder</c> media type with no
    /// file, so this is a <c>POST /media</c>. (<c>media-type/folder</c> exists but organises
    /// media <i>types</i>, which is a different noun and an easy trap.)
    /// </para>
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <param name="parentId">Parent folder id; null creates at the media root.</param>
    /// <param name="id">Client-supplied id for an idempotent create; null generates one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created folder, or a mapped failure.</returns>
    Task<UmbracoResponse<MediaItemResponse>> CreateMediaFolderAsync(
        string name,
        Guid? parentId = null,
        Guid? id = null,
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

    /// <summary>
    /// Walks the media tree (issue #89) and returns a flat, pre-order list carrying each node's
    /// depth and parent. Lists from the media root, or beneath <paramref name="parentId"/>.
    /// </summary>
    /// <param name="parentId">The node whose subtree to walk; null walks from the media root.</param>
    /// <param name="maxDepth">How many levels to descend (1 = direct children only). Bounded for safety.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subtree as a flat pre-order list, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetMediaTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    );

    /// <summary>
    /// Finds media whose name matches <paramref name="query"/> (issue #89), via the media search
    /// endpoint. Matching is the server's (contains, case-insensitive).
    /// </summary>
    /// <param name="query">The name text to search for.</param>
    /// <param name="parentId">Optional subtree to scope the search to; null searches everywhere.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of matching media, or a mapped failure.</returns>
    Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> FindMediaByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// Locates a media item by its name path from the media root (issue #89), e.g.
    /// <c>Images/Logos/Primary</c>. Each segment is matched against a child's name (case-insensitive,
    /// exact), descending one level per segment.
    /// </summary>
    /// <param name="path">A <c>/</c>-separated path of node names from the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matched node (a list of zero or one item), or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<MediaItemResponse>>> FindMediaByPathAsync(
        string path,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reorders a parent folder's child media items (issue #88). The supplied ids define the new
    /// order: the first id gets sort order 0, the next 1, and so on.
    /// </summary>
    /// <param name="parentId">The parent folder whose children to reorder; null reorders the media root.</param>
    /// <param name="orderedChildIds">Child media ids in the desired order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> SortMediaAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    );
}
