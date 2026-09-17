namespace Umbraco.Cli.Client;

/// <summary>Document (content) CRUD plus publish/unpublish.</summary>
public interface IContentClient
{
    Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(
        CreateContentRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteContentAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    );

    /// <summary>Lists the version history of a document (issue #58).</summary>
    /// <param name="documentId">The document whose versions to list.</param>
    /// <param name="culture">Culture to filter versions by; null for the invariant/default.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of versions mapped to <see cref="DocumentVersionResponse"/>.</returns>
    Task<UmbracoResponse<PagedResponse<DocumentVersionResponse>>> GetDocumentVersionsAsync(
        Guid documentId,
        string? culture = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Rolls a document back to a previous version (issue #58).</summary>
    /// <param name="versionId">The id of the version to roll back to.</param>
    /// <param name="culture">Culture to roll back; null for the invariant/default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RollbackDocumentVersionAsync(
        Guid versionId,
        string? culture = null,
        CancellationToken ct = default
    );

    /// <summary>Moves a document to the recycle bin (issue #67). Reversible via <see cref="RestoreContentAsync"/>.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> TrashContentAsync(Guid id, CancellationToken ct = default);

    /// <summary>Restores a document from the recycle bin (issue #67).</summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="parentId">Target parent to restore under; null restores to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    );

    /// <summary>Permanently empties the content recycle bin (issue #67). Irreversible.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> EmptyContentRecycleBinAsync(CancellationToken ct = default);

    /// <summary>Moves a document under a new parent (issue #67).</summary>
    /// <param name="id">The document id.</param>
    /// <param name="parentId">Target parent id; null moves to the content root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MoveContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    );

    /// <summary>Copies a document under a new parent (issue #67).</summary>
    /// <param name="id">The document id to copy.</param>
    /// <param name="parentId">Target parent id; null copies to the content root.</param>
    /// <param name="includeDescendants">Whether to copy descendants too.</param>
    /// <param name="relateToOriginal">Whether to create a relation to the original.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reorders a parent's child documents (issue #88). The supplied ids define the new order:
    /// the first id gets sort order 0, the next 1, and so on.
    /// </summary>
    /// <param name="parentId">The parent whose children to reorder; null reorders the content root.</param>
    /// <param name="orderedChildIds">Child document ids in the desired order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> SortContentAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    );

    /// <summary>Publishes a document and its descendants (issue #67).</summary>
    /// <param name="id">The root document id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes all.</param>
    /// <param name="includeUnpublishedDescendants">Whether to also publish descendants that were never published.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        CancellationToken ct = default
    );
}
