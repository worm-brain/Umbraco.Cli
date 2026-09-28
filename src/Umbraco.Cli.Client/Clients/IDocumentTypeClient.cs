namespace Umbraco.Cli.Client;

/// <summary>Document type (content type) read, create, and delete.</summary>
public interface IDocumentTypeClient
{
    Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a document type from the scalar fields the CLI exposes. Returns only the id;
    /// read the type back for what Umbraco saved (#314).
    /// </summary>
    /// <param name="request">The document type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id the document type was created with, or a mapped failure.</returns>
    Task<UmbracoResponse<Guid>> CreateDocumentTypeAsync(
        CreateDocumentTypeRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteDocumentTypeAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The aliases of several document types, in one call for a whole report (#293). No endpoint
    /// returns aliases in bulk, so each distinct type costs one read, cached for the client's life
    /// and shared with the content reads. Best-effort: a type that cannot be read is left out.
    /// </summary>
    /// <param name="ids">The document type ids; repeats are read once.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each readable type's alias, by id.</returns>
    Task<UmbracoResponse<IReadOnlyDictionary<Guid, string>>> GetDocumentTypeAliasesAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default
    );

    /// <summary>
    /// What deleting the document type would take with it (#287): its documents, counting the
    /// recycle bin, the types that use it as a composition, and whether it is an element type.
    /// The documents are counted with one walk of the content tree and the recycle bin per client,
    /// shared by every type checked.
    /// </summary>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The usage, or a mapped failure.</returns>
    Task<UmbracoResponse<TypeUsage>> GetDocumentTypeUsageAsync(
        Guid id,
        CancellationToken ct = default
    );
}
