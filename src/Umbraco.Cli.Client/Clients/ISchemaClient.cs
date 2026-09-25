using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Full-fidelity, raw-JSON access to Umbraco schema entities — document types, data
/// types, and templates — for the export/diff/apply pipeline (issue #68, ADR 0005).
///
/// The typed read methods on <see cref="IDocumentTypeClient"/>, <see cref="IDataTypeClient"/>
/// and <see cref="ITemplateClient"/> return **lossy** projection records: they drop
/// document-type properties/groups/compositions, data-type configuration <c>values</c>, and
/// template Razor <c>content</c>. Those records are fine for <c>list</c>/<c>get</c> display
/// but cannot round-trip a schema. These members instead return and accept the **verbatim
/// Management-API JSON body** (a <see cref="JsonNode"/>), so a schema can be exported and
/// re-applied without loss. See ADR 0005 §1 (raw-JSON passthrough).
///
/// Enumerating the entities is done through the existing typed paged list methods
/// (<c>Get*TypesAsync</c>), which carry the id; only the full per-entity body needs these raw
/// reads. Deletes reuse the existing typed <c>Delete*Async</c> methods.
/// </summary>
public interface ISchemaClient
{
    /// <summary>
    /// Enumerates the ids of <b>every</b> document type, walking the whole tree (issue #68).
    /// The tree contains organisational <b>folders</b> as well as document types, and document
    /// types can be nested inside folders — so this recurses through children and returns only
    /// real document-type ids (folders are descended into but never returned). This is why the
    /// typed <see cref="GetDocumentTypesAsync"/> (a single tree-root page, folders included) is
    /// unsuitable for export.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every document-type id, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDocumentTypeIdsAsync(
        CancellationToken ct = default
    );

    /// <summary>Enumerates the ids of every data type, recursing the tree and skipping folders (#68).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every data-type id, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDataTypeIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Enumerates the ids of every template, recursing the tree (#68). Templates nest by
    /// inheritance (a master template's children are the templates that inherit it), so nested
    /// items are real templates and are returned, not skipped.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every template id, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> GetTemplateIdsAsync(CancellationToken ct = default);

    /// <summary>Reads the verbatim <c>GET /document-type/{id}</c> body.</summary>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetDocumentTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Reads the verbatim <c>GET /data-type/{id}</c> body.</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetDataTypeRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reads the verbatim <c>GET /template/{id}</c> body.</summary>
    /// <param name="id">The template id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetTemplateRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a document type by POSTing a verbatim request body (<c>POST /document-type</c>).</summary>
    /// <param name="body">The full document-type JSON body (including a client-supplied <c>id</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateDocumentTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Updates a document type by PUTting a verbatim body (<c>PUT /document-type/{id}</c>, full replace).</summary>
    /// <param name="id">The document type id.</param>
    /// <param name="body">The full replacement document-type JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDocumentTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Creates a data type by POSTing a verbatim request body (<c>POST /data-type</c>).</summary>
    /// <param name="body">The full data-type JSON body (including a client-supplied <c>id</c> and <c>values</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateDataTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Updates a data type by PUTting a verbatim body (<c>PUT /data-type/{id}</c>, full replace).</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="body">The full replacement data-type JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDataTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Creates a template by POSTing a verbatim request body (<c>POST /template</c>).</summary>
    /// <param name="body">The full template JSON body (including a client-supplied <c>id</c> and Razor <c>content</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    /// <summary>
    /// Enumerates every media type id by walking the media-type tree (#186). Folders are skipped
    /// but descended into, as for document types.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every media type id, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMediaTypeIdsAsync(CancellationToken ct = default);

    /// <summary>Enumerates every member type id by walking the member-type tree (#186).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every member type id, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMemberTypeIdsAsync(
        CancellationToken ct = default
    );

    /// <summary>Reads a media type's verbatim body (<c>GET /media-type/{id}</c>).</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetMediaTypeRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reads a member type's verbatim body (<c>GET /member-type/{id}</c>).</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetMemberTypeRawAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a media type from a verbatim body (<c>POST /media-type</c>).</summary>
    /// <param name="body">The full media-type JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateMediaTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Replaces a media type from a verbatim body (<c>PUT /media-type/{id}</c>).</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="body">The full replacement body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateMediaTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Creates a member type from a verbatim body (<c>POST /member-type</c>).</summary>
    /// <param name="body">The full member-type JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateMemberTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Replaces a member type from a verbatim body (<c>PUT /member-type/{id}</c>).</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="body">The full replacement body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateMemberTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> CreateTemplateRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>Updates a template by PUTting a verbatim body (<c>PUT /template/{id}</c>, full replace).</summary>
    /// <param name="id">The template id.</param>
    /// <param name="body">The full replacement template JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateTemplateRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>
    /// Writes a schema item back from a body of the shape its raw GET returns (#201). By default
    /// the item is read and the body's top-level keys are laid over it, so a key the body leaves
    /// out keeps its value; <paramref name="replace"/> sends the body as the whole item instead.
    /// </summary>
    /// <param name="kind">The schema kind: a document, media or member type, a data type, or a template.</param>
    /// <param name="id">The item id.</param>
    /// <param name="body">The body; must be a JSON object.</param>
    /// <param name="replace">Send the body as the whole item rather than merging it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MergeSchemaItemAsync(
        EntityKind kind,
        Guid id,
        JsonNode body,
        bool replace = false,
        CancellationToken ct = default
    );
}
