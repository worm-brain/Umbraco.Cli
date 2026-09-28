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
    /// <summary>
    /// Resolves a data-type reference - a name (e.g. <c>Textstring</c>) or a GUID id - to its id.
    /// </summary>
    /// <param name="nameOrId">The data type name or its id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved data-type id.</returns>
    /// <exception cref="ApiException">No data type matches the name (404), or several do (409).</exception>
    private async Task<Guid> FindDataTypeIdAsync(string nameOrId, CancellationToken ct)
    {
        var search = await _api.Umbraco.Management.Api.V1.Item.DataType.Search.GetAsync(
            c =>
            {
                c.QueryParameters.Query = nameOrId;
                c.QueryParameters.Take = 100;
            },
            ct
        );
        // Two data types can share a name; that is refused rather than guessed (#250 Phase 3).
        return ReferenceMatch.Pick(
            EntityKind.DataType,
            nameOrId,
            (search?.Items ?? [])
                .Where(d => d.Id is not null)
                .Select(d => new ReferenceCandidate(d.Id!.Value, null, d.Name))
        );
    }
}
