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

    /// <summary>
    /// Writes a schema item back from a body of the shape its raw GET returns (#201). By default
    /// the item is read and the body's top-level keys are laid over it, so a key the body leaves
    /// out keeps its value; <see cref="WriteMode.Replace"/> sends the body as the whole item instead.
    /// </summary>
    /// <param name="kind">The schema kind: a document, media or member type, a data type, or a template.</param>
    /// <param name="id">The item id.</param>
    /// <param name="body">The body; must be a JSON object.</param>
    /// <param name="mode"><see cref="WriteMode.Replace"/> sends the body as the whole item rather than merging it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MergeSchemaItemAsync(
        EntityKind kind,
        Guid id,
        JsonNode body,
        WriteMode mode = WriteMode.Merge,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads a schema item's verbatim Management API body (<c>GET /{kind}/{id}</c>): what
    /// <c>get</c> prints and <c>schema export</c> writes, and the shape
    /// <see cref="MergeSchemaItemAsync"/> takes back.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="id">The item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body, or a mapped failure.</returns>
    Task<UmbracoResponse<JsonNode>> GetSchemaRawAsync(
        EntityKind kind,
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads many schema items' verbatim bodies, in the order of <paramref name="ids"/>: what
    /// <see cref="GetSchemaRawAsync"/> returns for each, in fewer requests where the kind has a
    /// batch read (document, media and member types and data types, #418) - 40 ids per request.
    /// Other kinds, and Umbraco before 17.3 (no batch endpoints), are read one id at a time.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="ids">The item ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Every body; or the first failure, including a 404 for an id the server does not have, so
    /// a caller never mistakes a partial read for the whole set.
    /// </returns>
    Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetSchemaRawManyAsync(
        EntityKind kind,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a schema item by POSTing a verbatim Management API body (<c>POST /{kind}</c>). The
    /// endpoint returns no body; the caller settles the id in the body first.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="body">The create body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateSchemaRawAsync(
        EntityKind kind,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads every language's verbatim body (<c>GET /language</c>, every page; #227). The list
    /// items are the whole language, so there is no per-item read. Languages have no id: the ISO
    /// code is the key.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every language body, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetLanguagesRawAsync(
        CancellationToken ct = default
    );

    /// <summary>Creates a language from a verbatim body (<c>POST /language</c>; #227).</summary>
    /// <param name="body">The language body, carrying its <c>isoCode</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> CreateLanguageRawAsync(
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>
    /// Replaces a language from a verbatim body (<c>PUT /language/{isoCode}</c>; #227). The ISO
    /// code is the route, so any <c>isoCode</c> in the body is not sent.
    /// </summary>
    /// <param name="isoCode">The language to update.</param>
    /// <param name="body">The language body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateLanguageRawAsync(
        string isoCode,
        JsonNode body,
        CancellationToken ct = default
    );

    /// <summary>
    /// Enumerates every dictionary item with its parent (<c>GET /dictionary</c>, every page;
    /// #227). The item read (<c>GET /dictionary/{id}</c>) has no parent, so the snapshot takes it
    /// from here.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item's id and parent id (null at the root), or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<DictionaryEntry>>> GetDictionaryEntriesAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads every member group's verbatim body (<c>GET /member-group</c>, every page; #227). The
    /// list items are the same model the by-id read returns, so there is no per-group read (#413).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every member group body, in list order, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetMemberGroupsRawAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Reads every user group's verbatim body (<c>GET /user-group</c>, every page; #227), from the
    /// list alone, as <see cref="GetMemberGroupsRawAsync"/> does (#413).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every user group body, in list order, or a mapped failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetUserGroupsRawAsync(
        CancellationToken ct = default
    );
}

/// <summary>A dictionary item's place in the dictionary tree (#227).</summary>
/// <param name="Id">The item id.</param>
/// <param name="ParentId">The parent item id, or null at the root.</param>
public sealed record DictionaryEntry(Guid Id, Guid? ParentId);
