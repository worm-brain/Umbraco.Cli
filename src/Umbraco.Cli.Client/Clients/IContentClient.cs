using System.Text.Json.Nodes;

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

    /// <summary>
    /// Updates a document (#178/#179). The document is read verbatim first and the request is
    /// overlaid onto it, so anything the request does not mention survives - including the
    /// template, which the Management API's replace-semantics PUT would otherwise clear.
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="request">The values, variants and (optionally) template to write.</param>
    /// <param name="mode">
    /// <see cref="WriteMode.Replace"/> makes the request's values and variants replace the
    /// document's wholesale instead of being merged into them. The template is still preserved
    /// unless the request sets one - dropping it was never intended behaviour (#178).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated content item, hydrated where possible, or a mapped failure.</returns>
    Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        WriteMode mode = WriteMode.Merge,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteContentAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reads a document's culture-and-hostname bindings (#180).</summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document's domains, or a mapped failure.</returns>
    Task<UmbracoResponse<DomainsResponse>> GetDomainsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Sets a document's culture-and-hostname bindings (#180).
    /// <para>
    /// The PUT replaces, so <paramref name="request"/> is the complete set. Without domains, a
    /// root published in several cultures is unreachable in all but the default one - Umbraco
    /// logs "published with multiple cultures, but no domains are configured".
    /// </para>
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="request">The complete set of domains and the default culture.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The domains as the instance holds them afterwards, or a mapped failure.</returns>
    Task<UmbracoResponse<DomainsResponse>> SetDomainsAsync(
        Guid id,
        SetDomainsRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// The cultures a publish of this document covers, resolved exactly as
    /// <see cref="PublishContentAsync"/> resolves them: <paramref name="cultures"/> when given,
    /// otherwise every culture the document varies by (read from the document). Callers pass the
    /// result straight to <see cref="PublishContentAsync"/> and report it, so what they report is
    /// what was sent (#325).
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">The cultures asked for; null/empty means every culture the document has.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The culture codes; empty for an invariant document. Or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<string>>> PublishCulturesAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Publishes a document (issue #79), optionally scheduling when it goes live and/or comes down
    /// (issue #90). The schedule rides the publish request's per-culture schedule, so a scheduled
    /// unpublish is expressed here via <paramref name="unpublishAt"/> rather than on the unpublish verb.
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">
    /// Cultures to publish; null/empty reads the document and publishes every culture it varies
    /// by. <c>"*"</c> is not a wildcard here - it is the invariant culture (#158).
    /// </param>
    /// <param name="publishAt">When to publish; null publishes immediately.</param>
    /// <param name="unpublishAt">When to unpublish again; null leaves it published.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        DateTimeOffset? publishAt = null,
        DateTimeOffset? unpublishAt = null,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lists the version history of a document (issue #58). With no culture, a document that
    /// varies by culture is listed across every culture it has, each row tagged (#209).
    /// </summary>
    /// <param name="documentId">The document whose versions to list.</param>
    /// <param name="culture">Culture to list versions for; null lists every culture the document has.</param>
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

    /// <summary>Reads one version of a document, values included, as raw JSON (#209).</summary>
    /// <param name="versionId">The version id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetDocumentVersionAsync(
        Guid versionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The document a version belongs to, via <c>GET document-version/{id}</c> (#233). A rollback
    /// names a version, so this is how a caller finds the document to publish afterwards.
    /// </summary>
    /// <param name="versionId">The version id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document id; a 404 when the version does not exist.</returns>
    Task<UmbracoResponse<Guid>> GetVersionDocumentIdAsync(
        Guid versionId,
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

    /// <summary>
    /// Restores a document from the recycle bin (issue #67). With no parent it goes back under the
    /// parent it was trashed from (#230).
    /// </summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="target">Where to restore to; null means <see cref="RestoreTarget.Original"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        RestoreTarget? target = null,
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

    /// <summary>Copies a document under a new parent and returns the copy's new id (issues #67, #91).</summary>
    /// <param name="id">The document id to copy.</param>
    /// <param name="parentId">Target parent id; null copies to the content root.</param>
    /// <param name="includeDescendants">Whether to copy descendants too.</param>
    /// <param name="relateToOriginal">Whether to create a relation to the original.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The copied document (with its new id), or a mapped failure.</returns>
    Task<UmbracoResponse<ContentItemResponse>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    );

    /// <summary>
    /// Walks the document tree (issue #89) and returns a flat, pre-order list carrying each node's
    /// depth and parent. Lists from the content root, or beneath <paramref name="parentId"/>.
    /// </summary>
    /// <param name="parentId">The node whose subtree to walk; null walks from the content root.</param>
    /// <param name="maxDepth">How many levels to descend (1 = direct children only). Bounded for safety.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subtree as a flat pre-order list, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetContentTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    );

    /// <summary>
    /// Finds documents whose name matches <paramref name="query"/> (issue #89), via the document
    /// search endpoint. Matching is the server's (contains, case-insensitive).
    /// </summary>
    /// <param name="query">The name text to search for.</param>
    /// <param name="parentId">Optional subtree to scope the search to; null searches everywhere.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of matching documents, or a mapped failure.</returns>
    Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> FindContentByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// Locates a document by its name path from the content root (issue #89), e.g.
    /// <c>Home/About/Team</c>. Each segment is matched against a child's name (case-insensitive,
    /// exact), descending one level per segment.
    /// </summary>
    /// <param name="path">A <c>/</c>-separated path of node names from the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matched node (a list of zero or one item), or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<ContentItemResponse>>> FindContentByPathAsync(
        string path,
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

    /// <summary>
    /// Publishes a document and its descendants (issue #67). The server runs the branch publish as a
    /// background task; the result carries its task id and completion state. With
    /// <paramref name="wait"/> the call polls the task to completion before returning (issue #90).
    /// </summary>
    /// <param name="id">The root document id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes all.</param>
    /// <param name="includeUnpublishedDescendants">Whether to also publish descendants that were never published.</param>
    /// <param name="wait">Whether to poll the background task until it completes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task id and completion state, or a mapped failure.</returns>
    Task<UmbracoResponse<PublishDescendantsResult>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        bool wait = false,
        CancellationToken ct = default
    );
}
