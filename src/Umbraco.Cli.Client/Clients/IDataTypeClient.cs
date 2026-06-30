namespace Umbraco.Cli.Client;

/// <summary>Data type read access.</summary>
public interface IDataTypeClient
{
    Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(Guid id, CancellationToken ct = default);
}
