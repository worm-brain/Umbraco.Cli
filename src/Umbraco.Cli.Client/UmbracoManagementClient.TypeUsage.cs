namespace Umbraco.Cli.Client;

/// <summary>
/// How much a document or media type delete would take with it (#287). Umbraco has no endpoint
/// that counts the items of a type (no filter by type; the item search runs on Examine, which is
/// not reliable enough to count), so the items are counted by walking the tree, whose items carry
/// their type, plus the recycle bin. The walk is done once per client and shared by every type
/// checked in the run, so a prune of several types, or a delete of several ids, costs one walk.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The documents of each type (tree plus recycle bin), walked once per client.</summary>
    private Task<Dictionary<Guid, int>>? _documentsByType;

    /// <summary>The media items of each type (tree plus recycle bin), walked once per client.</summary>
    private Task<Dictionary<Guid, int>>? _mediaByType;

    /// <inheritdoc />
    public Task<UmbracoResponse<TypeUsage>> GetDocumentTypeUsageAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var counts = await CachedCountsAsync(
                    () => _documentsByType,
                    t => _documentsByType = t,
                    CountDocumentsByTypeAsync,
                    ct
                );
                var composedBy = await _api
                    .Umbraco.Management.Api.V1.DocumentType[id]
                    .CompositionReferences.GetAsync(cancellationToken: ct);
                var type = await _api
                    .Umbraco.Management.Api.V1.DocumentType[id]
                    .GetAsync(cancellationToken: ct);
                return new TypeUsage(
                    counts.GetValueOrDefault(id),
                    [.. (composedBy ?? []).Select(c => c.Name ?? c.Id?.ToString() ?? "")],
                    type?.IsElement == true
                );
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<TypeUsage>> GetMediaTypeUsageAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var counts = await CachedCountsAsync(
                    () => _mediaByType,
                    t => _mediaByType = t,
                    CountMediaByTypeAsync,
                    ct
                );
                var composedBy = await _api
                    .Umbraco.Management.Api.V1.MediaType[id]
                    .CompositionReferences.GetAsync(cancellationToken: ct);
                return new TypeUsage(
                    counts.GetValueOrDefault(id),
                    [.. (composedBy ?? []).Select(c => c.Name ?? c.Id?.ToString() ?? "")]
                );
            }
        );

    /// <summary>
    /// Returns the cached per-type counts, starting the walk on first use. The task itself is
    /// cached, so checks started together (a delete of several ids runs them in parallel) share one
    /// walk; a walk that fails is dropped, so a later check asks again rather than reusing the
    /// failure.
    /// </summary>
    /// <param name="get">Reads the cache field.</param>
    /// <param name="set">Writes the cache field.</param>
    /// <param name="walk">Counts the items of every type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of items of each type.</returns>
    private static async Task<Dictionary<Guid, int>> CachedCountsAsync(
        Func<Task<Dictionary<Guid, int>>?> get,
        Action<Task<Dictionary<Guid, int>>?> set,
        Func<CancellationToken, Task<Dictionary<Guid, int>>> walk,
        CancellationToken ct
    )
    {
        var task = get();
        if (task is null)
        {
            task = walk(ct);
            set(task);
        }
        try
        {
            return await task;
        }
        catch
        {
            set(null);
            throw;
        }
    }

    /// <summary>
    /// Counts every document by its type: the whole content tree (its items carry their type),
    /// then the recycle bin, whose items Umbraco also deletes with their type.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of documents of each type.</returns>
    private async Task<Dictionary<Guid, int>> CountDocumentsByTypeAsync(CancellationToken ct)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var node in await WalkDocumentTreeAsync(parent: null, ct))
            Tally(counts, node.TypeId);
        await WalkRecycleBinAsync(
            async (parent, skip, take) =>
            {
                var page = parent is null
                    ? await _api.Umbraco.Management.Api.V1.RecycleBin.Document.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await _api.Umbraco.Management.Api.V1.RecycleBin.Document.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = parent;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return (
                    [
                        .. (page?.Items ?? []).Select(i =>
                            (i.Id ?? Guid.Empty, i.DocumentType?.Id, i.HasChildren ?? false)
                        ),
                    ],
                    (int)(page?.Total ?? 0)
                );
            },
            parent: null,
            counts
        );
        return counts;
    }

    /// <summary>The media twin of <see cref="CountDocumentsByTypeAsync"/>.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of media items of each type.</returns>
    private async Task<Dictionary<Guid, int>> CountMediaByTypeAsync(CancellationToken ct)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var node in await WalkMediaSnapshotTreeAsync(parent: null, ct))
            Tally(counts, node.TypeId);
        await WalkRecycleBinAsync(
            async (parent, skip, take) =>
            {
                var page = parent is null
                    ? await _api.Umbraco.Management.Api.V1.RecycleBin.Media.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await _api.Umbraco.Management.Api.V1.RecycleBin.Media.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = parent;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return (
                    [
                        .. (page?.Items ?? []).Select(i =>
                            (i.Id ?? Guid.Empty, i.MediaType?.Id, i.HasChildren ?? false)
                        ),
                    ],
                    (int)(page?.Total ?? 0)
                );
            },
            parent: null,
            counts
        );
        return counts;
    }

    /// <summary>
    /// Walks a recycle bin beneath <paramref name="parent"/>, counting each item under its type. A
    /// trashed item keeps its descendants under it, so items with children are descended into.
    /// </summary>
    /// <param name="page">Reads one page of a level: (parent, skip, take) to the items and the level's total.</param>
    /// <param name="parent">The trashed item whose children to list, or null for the bin's root.</param>
    /// <param name="counts">The per-type counts to add to.</param>
    /// <returns>A task that completes when the level and everything under it is counted.</returns>
    private static async Task WalkRecycleBinAsync(
        Func<
            Guid?,
            int,
            int,
            Task<(List<(Guid Id, Guid? TypeId, bool HasChildren)> Items, int Total)>
        > page,
        Guid? parent,
        Dictionary<Guid, int> counts
    )
    {
        var skip = 0;
        while (true)
        {
            var (items, total) = await page(parent, skip, TreePageSize);
            foreach (var item in items)
            {
                Tally(counts, item.TypeId);
                if (item.HasChildren)
                    await WalkRecycleBinAsync(page, item.Id, counts);
            }

            skip += items.Count;
            if (items.Count == 0 || skip >= total)
                break;
        }
    }

    /// <summary>Adds one item to its type's count; an item without a type is not counted.</summary>
    /// <param name="counts">The per-type counts.</param>
    /// <param name="typeId">The item's type id, or null.</param>
    private static void Tally(Dictionary<Guid, int> counts, Guid? typeId)
    {
        if (typeId is { } id)
            counts[id] = counts.GetValueOrDefault(id) + 1;
    }
}
