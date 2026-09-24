using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// Reading a document type or a data type by the key a human has (#159).
/// <para>
/// Both commands took a UUID while their help promised otherwise. The keys differ by kind and
/// that difference is real, not an inconsistency: a document type has an <b>alias</b>, a data
/// type does not - its <c>editorAlias</c> names the property editor behind it, which many data
/// types share - so a data type is addressed by <b>name</b>, as media types already are.
/// </para>
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeAsync(
        string aliasOrId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = Guid.TryParse(aliasOrId, out var parsed)
                    ? parsed
                    : await ResolveDocumentTypeIdAsync(aliasOrId, ct);
                return await ReadDocumentTypeAsync(id, ct);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DataTypeResponse>> GetDataTypeAsync(
        string nameOrId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = Guid.TryParse(nameOrId, out var parsed)
                    ? parsed
                    : await ResolveDataTypeIdAsync(nameOrId, ct);
                return await ReadDataTypeAsync(id, ct);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDocumentTypeRawAsync(
        string aliasOrId,
        System.Text.Json.Nodes.JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = Guid.TryParse(aliasOrId, out var parsed)
                    ? parsed
                    : await ResolveDocumentTypeIdAsync(aliasOrId, ct);
                return await SendRawJsonAsync(
                    Method.PUT,
                    $"umbraco/management/api/v1/document-type/{id}",
                    body,
                    ct
                );
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDataTypeRawAsync(
        string nameOrId,
        System.Text.Json.Nodes.JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = Guid.TryParse(nameOrId, out var parsed)
                    ? parsed
                    : await ResolveDataTypeIdAsync(nameOrId, ct);
                return await SendRawJsonAsync(
                    Method.PUT,
                    $"umbraco/management/api/v1/data-type/{id}",
                    body,
                    ct
                );
            }
        );

    /// <inheritdoc />
    public async Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        string nameOrId,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    )
    {
        // Two calls, so the resolve is guarded separately: a name that matches nothing must read
        // as a 404 naming the name, not as whatever the write would have said about the id.
        if (Guid.TryParse(nameOrId, out var id))
            return await UpdateDataTypeAsync(id, request, ct);

        var resolved = await GetDataTypeAsync(nameOrId, ct);
        return resolved.IsSuccess
            ? await UpdateDataTypeAsync(resolved.Data!.Id, request, ct)
            : UmbracoResponse<Empty>.FailureFrom(resolved);
    }

    /// <summary>
    /// Resolves a data-type reference - a name (e.g. <c>Textstring</c>) or a GUID id - to its id.
    /// </summary>
    /// <param name="nameOrId">The data type name or its id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved data-type id.</returns>
    /// <exception cref="ApiException">No data type matches the name (mapped to a 404).</exception>
    private async Task<Guid> ResolveDataTypeIdAsync(string nameOrId, CancellationToken ct)
    {
        var search = await _api.Umbraco.Management.Api.V1.Item.DataType.Search.GetAsync(
            c =>
            {
                c.QueryParameters.Query = nameOrId;
                c.QueryParameters.Take = 100;
            },
            ct
        );
        var match = (search?.Items ?? []).FirstOrDefault(d =>
            string.Equals(d.Name, nameOrId, StringComparison.OrdinalIgnoreCase)
        );
        return match?.Id
            ?? throw NotFound(
                $"No data type found with the name '{nameOrId}'. Use 'umbraco data-types list' "
                    + "to find one, or pass a data type id."
            );
    }
}
