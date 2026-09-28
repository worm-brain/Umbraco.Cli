using System.Text.Json.Nodes;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Advanced data-type verbs (copy/move/is-used/referenced-by and folder CRUD) and the property-type
/// usage check on <see cref="UmbracoManagementClient"/> (issue #121). Basic data-type CRUD lives in
/// the main client file; these extend it. referenced-by is surfaced as raw JSON because the API
/// returns a polymorphic union of reference kinds.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The Management API path prefix for the data-type resource (used by the raw seam).</summary>
    private const string DataTypePath = "umbraco/management/api/v1/data-type";

    // ── Data-type advanced ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<bool>> IsDataTypeUsedAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var used = await _api
                    .Umbraco.Management.Api.V1.DataType[id]
                    .IsUsed.GetAsync(cancellationToken: ct);
                return used ?? false;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetDataTypeReferencedByRawAsync(
        Guid id,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => GetRawJsonAsync($"{DataTypePath}/{id}/referenced-by?skip={skip}&take={take}", ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DataTypeResponse>> CopyDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    )
    {
        var body = new Gen.CopyDataTypeRequestModel
        {
            Target = targetId is { } t ? new Gen.ReferenceByIdModel { Id = t } : null,
        };
        return CopyViaLocationAsync(
            config => _api.Umbraco.Management.Api.V1.DataType[id].Copy.PostAsync(body, config, ct),
            newId => GetDataTypeByIdAsync(newId, ct),
            newId => new DataTypeResponse { Id = newId },
            "data type",
            ct
        );
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> MoveDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DataType[id]
                    .Move.PutAsync(
                        new Gen.MoveDataTypeRequestModel
                        {
                            Target = targetId is { } t
                                ? new Gen.ReferenceByIdModel { Id = t }
                                : null,
                        },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DataTypeFolderResponse>> GetDataTypeFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // A 200 with no body is not a folder (#119).
                var f =
                    await _api
                        .Umbraco.Management.Api.V1.DataType.Folder[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No data type folder found with id '{id}'.");
                return new DataTypeFolderResponse { Id = f.Id ?? id, Name = f.Name ?? "" };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DataTypeFolderResponse>> CreateDataTypeFolderAsync(
        CreateDataTypeFolderRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Client-generated id (the 201 body is empty), echoed back to the caller.
                var id = request.Id ?? Guid.NewGuid();
                await _api.Umbraco.Management.Api.V1.DataType.Folder.PostAsync(
                    new Gen.CreateFolderRequestModel
                    {
                        Id = id,
                        Name = request.Name,
                        Parent = request.ParentId is { } p
                            ? new Gen.ReferenceByIdModel { Id = p }
                            : null,
                    },
                    cancellationToken: ct
                );
                return new DataTypeFolderResponse { Id = id, Name = request.Name };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDataTypeFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DataType.Folder[id]
                    .PutAsync(
                        new Gen.UpdateFolderResponseModel { Name = name },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteDataTypeFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DataType.Folder[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Property-type usage ──────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Umbraco answers <c>false</c> for an alias the type does not have, which reads as "not in
    /// use, safe to remove" for a typo (#371). So the type and its compositions are read first,
    /// and an alias none of them has is refused as invalid_argument, listing the aliases they do
    /// have, the same way an unknown <c>--document-type</c> is.
    /// </remarks>
    public Task<UmbracoResponse<bool>> IsPropertyTypeUsedAsync(
        Guid contentTypeId,
        string propertyAlias,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var aliases = await DocumentTypePropertyAliasesAsync(contentTypeId, ct);
                if (!aliases.Contains(propertyAlias, StringComparer.OrdinalIgnoreCase))
                    throw new UnresolvedReferenceException(
                        $"Document type {contentTypeId} has no property with the alias "
                            + $"'{propertyAlias}' (compositions included). "
                            + (
                                aliases.Count == 0
                                    ? "It has no properties."
                                    : $"Its properties: {string.Join(", ", aliases)}."
                            ),
                        404
                    );

                var used = await _api.Umbraco.Management.Api.V1.PropertyType.IsUsed.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ContentTypeId = contentTypeId;
                        c.QueryParameters.PropertyAlias = propertyAlias;
                    },
                    ct
                );
                return used ?? false;
            }
        );

    /// <summary>
    /// Every property alias a document type has: its own, then those of its compositions,
    /// followed transitively (a composition can itself have compositions). Each type is read once.
    /// </summary>
    /// <param name="documentTypeId">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aliases, in the order found, without duplicates.</returns>
    /// <exception cref="ApiException">A type could not be read (e.g. a 404 for an unknown id).</exception>
    private async Task<List<string>> DocumentTypePropertyAliasesAsync(
        Guid documentTypeId,
        CancellationToken ct
    )
    {
        var aliases = new List<string>();
        var seen = new HashSet<Guid>();
        var pending = new Queue<Guid>([documentTypeId]);
        while (pending.TryDequeue(out var id))
        {
            // A composition cycle is not possible in Umbraco, but a type is never read twice.
            if (!seen.Add(id))
                continue;
            var type = await _api
                .Umbraco.Management.Api.V1.DocumentType[id]
                .GetAsync(cancellationToken: ct);
            foreach (var property in type?.Properties ?? [])
                if (
                    property.Alias is { } alias
                    && !aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
                )
                    aliases.Add(alias);
            foreach (var composition in type?.Compositions ?? [])
                if (composition.DocumentType?.Id is { } compositionId)
                    pending.Enqueue(compositionId);
        }
        return aliases;
    }
}
