using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Full-fidelity, raw-JSON access to Umbraco schema entities — document types, data
/// types, and templates — for the export/diff/apply pipeline (issue #68, ADR 0004).
///
/// The typed read methods on <see cref="IDocumentTypeClient"/>, <see cref="IDataTypeClient"/>
/// and <see cref="ITemplateClient"/> return **lossy** projection records: they drop
/// document-type properties/groups/compositions, data-type configuration <c>values</c>, and
/// template Razor <c>content</c>. Those records are fine for <c>list</c>/<c>get</c> display
/// but cannot round-trip a schema. These members instead return and accept the **verbatim
/// Management-API JSON body** (a <see cref="JsonNode"/>), so a schema can be exported and
/// re-applied without loss. See ADR 0004 §1 (raw-JSON passthrough).
///
/// Enumerating the entities is done through the existing typed paged list methods
/// (<c>Get*TypesAsync</c>), which carry the id; only the full per-entity body needs these raw
/// reads. Deletes reuse the existing typed <c>Delete*Async</c> methods.
/// </summary>
public interface ISchemaClient
{
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
}
