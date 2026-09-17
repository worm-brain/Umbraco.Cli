using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Child-reordering for content and media (issue #88). Both nouns share the same
/// <c>PUT {document,media}/sort</c> request shape, so the body building and the ordered-id →
/// <c>sortOrder</c> mapping live in one place; each verb only supplies which endpoint to call.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> SortContentAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    ) =>
        SortAsync(
            (body, c) =>
                _api.Umbraco.Management.Api.V1.Document.Sort.PutAsync(body, cancellationToken: c),
            parentId,
            orderedChildIds,
            ct
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> SortMediaAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    ) =>
        SortAsync(
            (body, c) =>
                _api.Umbraco.Management.Api.V1.Media.Sort.PutAsync(body, cancellationToken: c),
            parentId,
            orderedChildIds,
            ct
        );

    /// <summary>
    /// Builds the shared sorting request (parent + ordered ids as ascending <c>sortOrder</c>) and
    /// PUTs it through <paramref name="put"/> - the one endpoint that differs between the two nouns.
    /// </summary>
    /// <param name="put">Sends the sorting body to the noun's sort endpoint.</param>
    /// <param name="parentId">The parent whose children to reorder; null reorders the root.</param>
    /// <param name="orderedChildIds">Child ids in the desired order (first gets sort order 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    private Task<UmbracoResponse<Empty>> SortAsync(
        Func<Gen.SortingRequestModel, CancellationToken, Task> put,
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.SortingRequestModel
                {
                    Parent = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                    Sorting = orderedChildIds
                        .Select(
                            (childId, index) =>
                                new Gen.ItemSortingRequestModel { Id = childId, SortOrder = index }
                        )
                        .ToList(),
                };
                await put(body, ct);
                return Empty.Value;
            }
        );
}
