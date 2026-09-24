namespace Umbraco.Cli.Client;

/// <summary>Document type (content type) read, create, and delete.</summary>
public interface IDocumentTypeClient
{
    Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>
    /// Gets a document type by its alias or its id (#159). A value that parses as a GUID is used
    /// directly; anything else is resolved as an alias.
    /// </summary>
    /// <param name="aliasOrId">The document type alias (e.g. <c>blogPost</c>) or its id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document type, or a mapped failure.</returns>
    Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeAsync(
        string aliasOrId,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(
        CreateDocumentTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Replaces a document type addressed by alias or id with a verbatim body (#161). The alias
    /// is resolved here rather than in the command, so a caller never has to look an id up just
    /// to write back what it has just read, and so the resolution failure surfaces as this
    /// call's own failure.
    /// </summary>
    /// <param name="aliasOrId">The document type alias (e.g. <c>blogPost</c>) or its id.</param>
    /// <param name="body">The full replacement document-type JSON body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDocumentTypeRawAsync(
        string aliasOrId,
        System.Text.Json.Nodes.JsonNode body,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteDocumentTypeAsync(Guid id, CancellationToken ct = default);
}
