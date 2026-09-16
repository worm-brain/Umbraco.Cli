namespace Umbraco.Cli.Client;

/// <summary>Examine index and searcher access (issue #121): list/inspect indexes, rebuild, and query searchers.</summary>
public interface IExamineClient
{
    /// <summary>Lists the Examine indexes.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of indexes.</returns>
    Task<UmbracoResponse<PagedResponse<IndexResponse>>> GetIndexersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a single index by name.</summary>
    /// <param name="name">The index name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The index.</returns>
    Task<UmbracoResponse<IndexResponse>> GetIndexerAsync(
        string name,
        CancellationToken ct = default
    );

    /// <summary>Rebuilds an index by name (an expensive server-side action).</summary>
    /// <param name="name">The index name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RebuildIndexAsync(string name, CancellationToken ct = default);

    /// <summary>Lists the Examine searchers.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of searchers.</returns>
    Task<UmbracoResponse<PagedResponse<SearcherResponse>>> GetSearchersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Queries a searcher for a term.</summary>
    /// <param name="name">The searcher name.</param>
    /// <param name="term">The query term.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of search results.</returns>
    Task<UmbracoResponse<PagedResponse<SearchResultResponse>>> QuerySearcherAsync(
        string name,
        string term,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );
}

/// <summary>Read-only image URL generation (issue #121).</summary>
public interface IImagingClient
{
    /// <summary>Gets resized image URLs for the given media items.</summary>
    /// <param name="mediaIds">The media item ids.</param>
    /// <param name="width">Target width in pixels, if any.</param>
    /// <param name="height">Target height in pixels, if any.</param>
    /// <param name="mode">The crop/resize mode, if any.</param>
    /// <param name="format">The output image format (e.g. webp), if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resized URLs per media item (a bare list, not paged).</returns>
    Task<UmbracoResponse<IReadOnlyList<MediaResizeUrlResponse>>> GetResizeUrlsAsync(
        IReadOnlyList<Guid> mediaIds,
        int? width = null,
        int? height = null,
        ImageResizeMode? mode = null,
        string? format = null,
        CancellationToken ct = default
    );
}

/// <summary>Read-only property-type usage check (issue #121).</summary>
public interface IPropertyTypeClient
{
    /// <summary>Checks whether a property (by content type + alias) is in use.</summary>
    /// <param name="contentTypeId">The content type id the property belongs to.</param>
    /// <param name="propertyAlias">The property alias.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the property is used.</returns>
    Task<UmbracoResponse<bool>> IsPropertyTypeUsedAsync(
        Guid contentTypeId,
        string propertyAlias,
        CancellationToken ct = default
    );
}
