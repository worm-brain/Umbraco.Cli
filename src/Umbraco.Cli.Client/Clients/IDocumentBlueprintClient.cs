using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Document-blueprint coverage (issue #113 - parity with the MCP's document-blueprint tools):
/// tree listing, full-fidelity get/scaffold/create (raw JSON), typed update/move/delete,
/// scaffold-from-document, and blueprint folder CRUD.
/// </summary>
public interface IDocumentBlueprintClient
{
    /// <summary>
    /// Lists blueprints from the blueprint tree: the root when <paramref name="parentId"/> is null,
    /// otherwise the children of that folder.
    /// </summary>
    /// <param name="parentId">Parent folder id to list children of; null for the root.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of blueprint tree items.</returns>
    Task<UmbracoResponse<PagedResponse<DocumentBlueprintTreeItem>>> GetDocumentBlueprintsAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a blueprint by id as raw JSON (full fidelity, including all property values).</summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blueprint's verbatim JSON body.</returns>
    Task<UmbracoResponse<JsonNode>> GetDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Gets the scaffold for a blueprint as raw JSON: the pre-filled create template Umbraco would
    /// use to start a new document from this blueprint.
    /// </summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scaffold's verbatim JSON body.</returns>
    Task<UmbracoResponse<JsonNode>> ScaffoldDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a blueprint, then best-effort re-reads it so the returned JSON is fully hydrated.
    /// </summary>
    /// <param name="request">The blueprint to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created blueprint's JSON (hydrated, or a minimal <c>{ id }</c>), or a failure.</returns>
    Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintAsync(
        CreateDocumentBlueprintRequest request,
        CancellationToken ct = default
    );

    /// <summary>Creates a blueprint from an existing document, then best-effort hydrates it.</summary>
    /// <param name="request">The source document and new-blueprint details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created blueprint's JSON (hydrated, or a minimal <c>{ id }</c>), or a failure.</returns>
    Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintFromDocumentAsync(
        CreateBlueprintFromDocumentRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a blueprint's values and variants by id.</summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="request">The values and variants to write.</param>
    /// <param name="replace">
    /// When false (the default) the request's values and variants are merged into the blueprint's,
    /// as <c>content update</c> does (#242); when true they replace them.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDocumentBlueprintAsync(
        Guid id,
        UpdateDocumentBlueprintRequest request,
        bool replace = false,
        CancellationToken ct = default
    );

    /// <summary>Deletes a blueprint by id.</summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Moves a blueprint under a target folder (or to the root when null).</summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="targetId">The destination folder id, or null for the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MoveDocumentBlueprintAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    );

    /// <summary>Gets a blueprint folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The folder.</returns>
    Task<UmbracoResponse<BlueprintFolderResponse>> GetBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a blueprint folder.</summary>
    /// <param name="request">The folder to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created folder (echoed with its id), or a mapped failure.</returns>
    Task<UmbracoResponse<BlueprintFolderResponse>> CreateBlueprintFolderAsync(
        CreateBlueprintFolderRequest request,
        CancellationToken ct = default
    );

    /// <summary>Renames a blueprint folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="name">The new folder name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateBlueprintFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    );

    /// <summary>Deletes a blueprint folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    );
}
