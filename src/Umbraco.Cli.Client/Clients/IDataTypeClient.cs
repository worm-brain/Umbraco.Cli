namespace Umbraco.Cli.Client;

/// <summary>Data type read access.</summary>
public interface IDataTypeClient
{
    Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0,
        int take = 20,
        Guid? parentId = null,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a data type (issue #59).</summary>
    /// <param name="request">The data type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created data type (with the generated id), or a mapped failure.</returns>
    Task<UmbracoResponse<DataTypeResponse>> CreateDataTypeAsync(
        CreateDataTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a data type by id (issue #59).</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="request">The replacement name + editor aliases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        Guid id,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Updates a data type addressed by name or id (#159/#169). The name is resolved here rather
    /// than in the command, so the lookup and the write are one operation to the caller.
    /// </summary>
    /// <param name="nameOrId">The data type name (e.g. <c>Textstring</c>) or its id.</param>
    /// <param name="request">The replacement name + editor aliases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        string nameOrId,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a data type by id (issue #59).</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDataTypeAsync(Guid id, CancellationToken ct = default);

    // ── Advanced verbs (issue #121) ──────────────────────────────────────────────

    /// <summary>Checks whether a data type is in use by any content type.</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the data type is used.</returns>
    Task<UmbracoResponse<bool>> IsDataTypeUsedAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists what references a data type, as raw JSON. The Management API returns a polymorphic union
    /// of reference kinds (documents, media, content types, ...), so it is surfaced verbatim.
    /// </summary>
    /// <param name="id">The data type id.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The references as a verbatim JSON document.</returns>
    Task<UmbracoResponse<System.Text.Json.Nodes.JsonNode>> GetDataTypeReferencedByRawAsync(
        Guid id,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Copies a data type, optionally under a target folder.</summary>
    /// <param name="id">The data type id to copy.</param>
    /// <param name="targetId">The destination folder id, or null for the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<DataTypeResponse>> CopyDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    );

    /// <summary>Moves a data type under a target folder (or to the root when null).</summary>
    /// <param name="id">The data type id to move.</param>
    /// <param name="targetId">The destination folder id, or null for the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> MoveDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    );

    /// <summary>Gets a data-type folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The folder.</returns>
    Task<UmbracoResponse<DataTypeFolderResponse>> GetDataTypeFolderAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a data-type folder.</summary>
    /// <param name="request">The folder to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created folder (echoed with its id), or a mapped failure.</returns>
    Task<UmbracoResponse<DataTypeFolderResponse>> CreateDataTypeFolderAsync(
        CreateDataTypeFolderRequest request,
        CancellationToken ct = default
    );

    /// <summary>Renames a data-type folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="name">The new folder name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateDataTypeFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    );

    /// <summary>Deletes a data-type folder by id.</summary>
    /// <param name="id">The folder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDataTypeFolderAsync(Guid id, CancellationToken ct = default);
}
