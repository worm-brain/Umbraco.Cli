namespace Umbraco.Cli.Client;

/// <summary>Data type read access.</summary>
public interface IDataTypeClient
{
    Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0,
        int take = 20,
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

    /// <summary>Deletes a data type by id (issue #59).</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDataTypeAsync(Guid id, CancellationToken ct = default);
}
