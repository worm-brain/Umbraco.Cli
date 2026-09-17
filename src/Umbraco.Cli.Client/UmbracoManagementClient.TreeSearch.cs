using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Tree traversal and find-by-name/path for content and media (issue #89). Kept in its own partial
/// so the traversal helpers stay together. The tree walk reuses the same <c>tree/{document,media}</c>
/// root/children endpoints the single-level list uses, descending client-side to a bounded depth;
/// find-by-name defers to the server search endpoint, while find-by-path walks name segment by
/// segment. Reuses <c>DocumentName</c>/<c>MapDocumentTreeItem</c>/<c>MapMediaTreeItem</c> and the
/// shared <c>TreePageSize</c>.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Hard ceiling on tree-walk depth (issue #89). A safety cap so a mis-supplied or unbounded
    /// depth can never recurse without limit, independent of the content tree's real nesting.
    /// </summary>
    private const int MaxTreeDepth = 50;

    /// <summary>Clamps a requested depth into <c>[1, <see cref="MaxTreeDepth"/>]</c>.</summary>
    /// <param name="requested">The caller-requested maximum depth.</param>
    /// <returns>The depth to actually walk to.</returns>
    private static int ClampDepth(int requested) => Math.Clamp(requested, 1, MaxTreeDepth);

    // ── Content tree + find ────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetContentTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<TreeItem>>(
            ct,
            async () => await WalkContentTreeAsync(parentId, depth: 1, ClampDepth(maxDepth), ct)
        );

    /// <summary>
    /// Recursively lists the document tree beneath <paramref name="parent"/> in pre-order, tagging
    /// each node with its depth and parent, and stopping at <paramref name="maxDepth"/>. Every level
    /// is paged client-side.
    /// </summary>
    /// <param name="parent">The parent to list beneath; null for the content root.</param>
    /// <param name="depth">The depth of the level being listed (1 for the first level).</param>
    /// <param name="maxDepth">The deepest level to descend to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subtree as a flat pre-order list.</returns>
    private async Task<List<TreeItem>> WalkContentTreeAsync(
        Guid? parent,
        int depth,
        int maxDepth,
        CancellationToken ct
    )
    {
        var nodes = new List<TreeItem>();
        var skip = 0;
        while (true)
        {
            var paged = parent is null
                ? await _api.Umbraco.Management.Api.V1.Tree.Document.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                )
                : await _api.Umbraco.Management.Api.V1.Tree.Document.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parent;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                );

            var items = paged?.Items ?? [];
            foreach (var item in items)
            {
                var id = item.Id ?? Guid.Empty;
                var hasChildren = item.HasChildren ?? false;
                nodes.Add(
                    new TreeItem
                    {
                        Id = id,
                        Name = DocumentName(item),
                        ParentId = parent,
                        Depth = depth,
                        HasChildren = hasChildren,
                    }
                );
                // Pre-order: descend only while there is depth budget left.
                if (hasChildren && depth < maxDepth)
                    nodes.AddRange(await WalkContentTreeAsync(id, depth + 1, maxDepth, ct));
            }

            skip += items.Count;
            if (items.Count == 0 || skip >= (int)(paged?.Total ?? 0))
                break;
        }
        return nodes;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> FindContentByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Item.Document.Search.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Query = query;
                        if (parentId is { } p)
                            c.QueryParameters.ParentId = p;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<ContentItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapDocumentSearchItem).ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<ContentItemResponse>>> FindContentByPathAsync(
        string path,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<ContentItemResponse>>(
            ct,
            async () =>
            {
                var segments = SplitPath(path);
                if (segments.Length == 0)
                    return [];

                Guid? parent = null;
                Gen.DocumentTreeItemResponseModel? matched = null;
                foreach (var segment in segments)
                {
                    matched = await FindChildDocumentAsync(parent, segment, ct);
                    if (matched is null)
                        return [];
                    parent = matched.Id;
                }
                return [MapDocumentTreeItem(matched!)];
            }
        );

    /// <summary>Finds the first direct child document of <paramref name="parent"/> whose name matches
    /// <paramref name="name"/> (case-insensitive), paging a level at a time.</summary>
    /// <param name="parent">The parent to search under; null for the content root.</param>
    /// <param name="name">The child name to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching tree item, or null if no child matched.</returns>
    private async Task<Gen.DocumentTreeItemResponseModel?> FindChildDocumentAsync(
        Guid? parent,
        string name,
        CancellationToken ct
    )
    {
        var skip = 0;
        while (true)
        {
            var paged = parent is null
                ? await _api.Umbraco.Management.Api.V1.Tree.Document.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                )
                : await _api.Umbraco.Management.Api.V1.Tree.Document.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parent;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                );

            var items = paged?.Items ?? [];
            var hit = items.FirstOrDefault(i =>
                string.Equals(DocumentName(i), name, StringComparison.OrdinalIgnoreCase)
            );
            if (hit is not null)
                return hit;

            skip += items.Count;
            if (items.Count == 0 || skip >= (int)(paged?.Total ?? 0))
                return null;
        }
    }

    // ── Media tree + find ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetMediaTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<TreeItem>>(
            ct,
            async () => await WalkMediaTreeAsync(parentId, depth: 1, ClampDepth(maxDepth), ct)
        );

    /// <summary>Media counterpart of <see cref="WalkContentTreeAsync"/> over the media tree.</summary>
    /// <param name="parent">The parent to list beneath; null for the media root.</param>
    /// <param name="depth">The depth of the level being listed (1 for the first level).</param>
    /// <param name="maxDepth">The deepest level to descend to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subtree as a flat pre-order list.</returns>
    private async Task<List<TreeItem>> WalkMediaTreeAsync(
        Guid? parent,
        int depth,
        int maxDepth,
        CancellationToken ct
    )
    {
        var nodes = new List<TreeItem>();
        var skip = 0;
        while (true)
        {
            var paged = parent is null
                ? await _api.Umbraco.Management.Api.V1.Tree.Media.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                )
                : await _api.Umbraco.Management.Api.V1.Tree.Media.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parent;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                );

            var items = paged?.Items ?? [];
            foreach (var item in items)
            {
                var id = item.Id ?? Guid.Empty;
                var hasChildren = item.HasChildren ?? false;
                nodes.Add(
                    new TreeItem
                    {
                        Id = id,
                        Name = MediaName(item),
                        ParentId = parent,
                        Depth = depth,
                        HasChildren = hasChildren,
                    }
                );
                if (hasChildren && depth < maxDepth)
                    nodes.AddRange(await WalkMediaTreeAsync(id, depth + 1, maxDepth, ct));
            }

            skip += items.Count;
            if (items.Count == 0 || skip >= (int)(paged?.Total ?? 0))
                break;
        }
        return nodes;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> FindMediaByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Item.Media.Search.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Query = query;
                        if (parentId is { } p)
                            c.QueryParameters.ParentId = p;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<MediaItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapMediaSearchItem).ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<MediaItemResponse>>> FindMediaByPathAsync(
        string path,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<MediaItemResponse>>(
            ct,
            async () =>
            {
                var segments = SplitPath(path);
                if (segments.Length == 0)
                    return [];

                Guid? parent = null;
                Gen.MediaTreeItemResponseModel? matched = null;
                foreach (var segment in segments)
                {
                    matched = await FindChildMediaAsync(parent, segment, ct);
                    if (matched is null)
                        return [];
                    parent = matched.Id;
                }
                return [MapMediaTreeItem(matched!)];
            }
        );

    /// <summary>Media counterpart of <see cref="FindChildDocumentAsync"/>.</summary>
    /// <param name="parent">The parent to search under; null for the media root.</param>
    /// <param name="name">The child name to match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching media tree item, or null if no child matched.</returns>
    private async Task<Gen.MediaTreeItemResponseModel?> FindChildMediaAsync(
        Guid? parent,
        string name,
        CancellationToken ct
    )
    {
        var skip = 0;
        while (true)
        {
            var paged = parent is null
                ? await _api.Umbraco.Management.Api.V1.Tree.Media.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                )
                : await _api.Umbraco.Management.Api.V1.Tree.Media.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parent;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                );

            var items = paged?.Items ?? [];
            var hit = items.FirstOrDefault(i =>
                string.Equals(MediaName(i), name, StringComparison.OrdinalIgnoreCase)
            );
            if (hit is not null)
                return hit;

            skip += items.Count;
            if (items.Count == 0 || skip >= (int)(paged?.Total ?? 0))
                return null;
        }
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    /// <summary>The display name of a media tree item (its first variant name).</summary>
    /// <param name="item">The generated media tree item.</param>
    /// <returns>The name, or an empty string when absent.</returns>
    private static string MediaName(Gen.MediaTreeItemResponseModel item) =>
        (item.Variants ?? []).FirstOrDefault()?.Name ?? "";

    /// <summary>Maps a generated document search item onto the command-facing <see cref="ContentItemResponse"/>.</summary>
    /// <param name="item">The generated document search item.</param>
    /// <returns>The mapped content item.</returns>
    private static ContentItemResponse MapDocumentSearchItem(Gen.DocumentItemResponseModel item) =>
        new()
        {
            Id = item.Id ?? Guid.Empty,
            Name = (item.Variants ?? []).FirstOrDefault()?.Name ?? "",
            ContentType = item.DocumentType?.Id is { } dtId
                ? new ContentTypeReference { Id = dtId }
                : null,
            Parent = item.Parent?.Id is { } pId ? new ContentParentReference { Id = pId } : null,
            IsPublished = (item.Variants ?? []).Any(v =>
                v.State
                    is Gen.DocumentVariantStateModel.Published
                        or Gen.DocumentVariantStateModel.PublishedPendingChanges
            ),
        };

    /// <summary>Maps a generated media search item onto the command-facing <see cref="MediaItemResponse"/>.</summary>
    /// <param name="item">The generated media search item.</param>
    /// <returns>The mapped media item.</returns>
    private static MediaItemResponse MapMediaSearchItem(Gen.MediaItemResponseModel item) =>
        new()
        {
            Id = item.Id ?? Guid.Empty,
            Name = (item.Variants ?? []).FirstOrDefault()?.Name ?? "",
            MediaType = item.MediaType?.Id is { } mtId
                ? new ContentTypeReference { Id = mtId }
                : null,
            Parent = item.Parent?.Id is { } pId ? new ContentParentReference { Id = pId } : null,
        };

    /// <summary>Splits a <c>/</c>-separated name path into trimmed, non-empty segments.</summary>
    /// <param name="path">The path text.</param>
    /// <returns>The path segments in order.</returns>
    private static string[] SplitPath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
