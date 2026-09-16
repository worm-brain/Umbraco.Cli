using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>A document's tree placement: its id and its parent (null at the content root).</summary>
/// <param name="Id">The document id.</param>
/// <param name="Parent">The parent document id, or null for a content-root document.</param>
public readonly record struct ContentTreeNode(Guid Id, Guid? Parent);

/// <summary>
/// Raw-JSON document access for the content export/diff/apply pipeline (issue #100, ADR 0006).
/// These carry the verbatim Management-API document body so nothing is dropped (all variants,
/// property values, doc-type and template references) - the typed content methods project a
/// lossy summary and cannot round-trip. Placement is returned separately by
/// <see cref="GetDocumentTreeAsync"/> because a document's raw GET body does not carry its parent.
/// </summary>
public interface IContentSnapshotClient
{
    /// <summary>
    /// Enumerates a content subtree in pre-order (a parent always precedes its children),
    /// returning each document's id and parent. With no <paramref name="root"/> it walks the whole
    /// content tree; with one it returns that document as a portable top-level node (parent null)
    /// followed by its descendants.
    /// </summary>
    /// <param name="root">The subtree root to export, or null for the whole content tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The documents' id/parent placements in pre-order.</returns>
    Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetDocumentTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    );

    /// <summary>Gets a document's verbatim <c>GET /document/{id}</c> body.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw document body.</returns>
    Task<UmbracoResponse<JsonNode>> GetDocumentRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a document from a verbatim body via <c>POST /document</c> (client-supplied id preserved).</summary>
    /// <param name="body">The raw document body (must carry its <c>id</c> and <c>parent</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateDocumentRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Updates a document from a verbatim body via <c>PUT /document/{id}</c>.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="body">The raw document body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDocumentRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );
}
