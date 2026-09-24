using System.Runtime.CompilerServices;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Tree traversal and find-by-name/path for content and media (issue #89). The paging, recursion,
/// and path-walking are shared generic helpers; content and media differ only in which
/// <c>tree/{document,media}</c> endpoint fetches a page and how a node's id/name/has-children are
/// read. find-by-name defers to the server search endpoints. Reuses <c>DocumentName</c> /
/// <c>MapDocumentTreeItem</c> / <c>MapMediaTreeItem</c> and the shared <c>TreePageSize</c>.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Hard ceiling on tree-walk depth (issue #89). A safety cap so a mis-supplied or unbounded
    /// depth can never recurse without limit, independent of the tree's real nesting.
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
            async () =>
                await WalkTreeAsync(
                    parentId,
                    depth: 1,
                    ClampDepth(maxDepth),
                    FetchDocumentTreePageAsync,
                    ProjectDocument,
                    ct
                )
        );

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
                var match = await FindByPathAsync(
                    path,
                    FetchDocumentTreePageAsync,
                    ProjectDocument,
                    ct
                );
                return match is null ? [] : [MapDocumentTreeItem(match)];
            }
        );

    // ── Media tree + find ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetMediaTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<TreeItem>>(
            ct,
            async () =>
                await WalkTreeAsync(
                    parentId,
                    depth: 1,
                    ClampDepth(maxDepth),
                    FetchMediaTreePageAsync,
                    ProjectMedia,
                    ct
                )
        );

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
                var match = await FindByPathAsync(path, FetchMediaTreePageAsync, ProjectMedia, ct);
                return match is null ? [] : [MapMediaTreeItem(match)];
            }
        );

    // ── Shared traversal ───────────────────────────────────────────────────────

    /// <summary>A page fetcher: reads one page of a tree level (root when parent is null).</summary>
    /// <typeparam name="TItem">The generated tree-item type.</typeparam>
    private delegate Task<(IReadOnlyList<TItem> Items, int Total)> FetchTreePage<TItem>(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    );

    /// <summary>A projector: reads a node's id, display name, and whether it has children.</summary>
    /// <typeparam name="TItem">The generated tree-item type.</typeparam>
    private delegate (Guid Id, string Name, bool HasChildren) ProjectNode<TItem>(TItem item);

    /// <summary>
    /// Enumerates one tree level in order, transparently paging with <see cref="TreePageSize"/> until
    /// the level is exhausted. Breaking out of the enumeration stops paging.
    /// </summary>
    /// <typeparam name="TItem">The generated tree-item type.</typeparam>
    /// <param name="parent">The parent whose children to enumerate; null for the root level.</param>
    /// <param name="fetchPage">Reads one page of the level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item at the level, in order.</returns>
    private async IAsyncEnumerable<TItem> EnumerateTreeLevelAsync<TItem>(
        Guid? parent,
        FetchTreePage<TItem> fetchPage,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var skip = 0;
        while (true)
        {
            var (items, total) = await fetchPage(parent, skip, TreePageSize, ct);
            foreach (var item in items)
                yield return item;
            skip += items.Count;
            if (items.Count == 0 || skip >= total)
                yield break;
        }
    }

    /// <summary>
    /// Walks a tree beneath <paramref name="parent"/> in pre-order into a flat list, tagging each
    /// node with its depth and parent and stopping at <paramref name="maxDepth"/>.
    /// </summary>
    /// <typeparam name="TItem">The generated tree-item type.</typeparam>
    /// <param name="parent">The parent to list beneath; null for the root.</param>
    /// <param name="depth">The depth of the level being listed (1 for the first level).</param>
    /// <param name="maxDepth">The deepest level to descend to.</param>
    /// <param name="fetchPage">Reads one page of a level.</param>
    /// <param name="project">Reads a node's id/name/has-children.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subtree as a flat pre-order list.</returns>
    private async Task<List<TreeItem>> WalkTreeAsync<TItem>(
        Guid? parent,
        int depth,
        int maxDepth,
        FetchTreePage<TItem> fetchPage,
        ProjectNode<TItem> project,
        CancellationToken ct
    )
    {
        var nodes = new List<TreeItem>();
        await foreach (var item in EnumerateTreeLevelAsync(parent, fetchPage, ct))
        {
            var (id, name, hasChildren) = project(item);
            nodes.Add(
                new TreeItem
                {
                    Id = id,
                    Name = name,
                    ParentId = parent,
                    Depth = depth,
                    HasChildren = hasChildren,
                }
            );
            // Pre-order: descend only while there is depth budget left.
            if (hasChildren && depth < maxDepth)
                nodes.AddRange(
                    await WalkTreeAsync(id, depth + 1, maxDepth, fetchPage, project, ct)
                );
        }
        return nodes;
    }

    /// <summary>
    /// Locates a node by a <c>/</c>-separated name path from the root, descending one level per
    /// segment and matching a child's name case-insensitively.
    /// </summary>
    /// <typeparam name="TItem">The generated tree-item type.</typeparam>
    /// <param name="path">The name path.</param>
    /// <param name="fetchPage">Reads one page of a level.</param>
    /// <param name="project">Reads a node's id/name/has-children.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matched item, or null when a segment matches nothing.</returns>
    private async Task<TItem?> FindByPathAsync<TItem>(
        string path,
        FetchTreePage<TItem> fetchPage,
        ProjectNode<TItem> project,
        CancellationToken ct
    )
        where TItem : class
    {
        var segments = SplitPath(path);
        if (segments.Length == 0)
            return null;

        Guid? parent = null;
        TItem? matched = null;
        foreach (var segment in segments)
        {
            matched = null;
            await foreach (var item in EnumerateTreeLevelAsync(parent, fetchPage, ct))
            {
                var (id, name, _) = project(item);
                if (string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    matched = item;
                    parent = id;
                    break;
                }
            }
            if (matched is null)
                return null;
        }
        return matched;
    }

    /// <summary>Reads one page of the document tree (root when <paramref name="parent"/> is null).</summary>
    /// <param name="parent">The parent to list beneath; null for the content root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's items and the level's total.</returns>
    private async Task<(
        IReadOnlyList<Gen.DocumentTreeItemResponseModel> Items,
        int Total
    )> FetchDocumentTreePageAsync(Guid? parent, int skip, int take, CancellationToken ct)
    {
        var paged = parent is null
            ? await _api.Umbraco.Management.Api.V1.Tree.Document.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            )
            : await _api.Umbraco.Management.Api.V1.Tree.Document.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        return (paged?.Items ?? [], (int)(paged?.Total ?? 0));
    }

    /// <summary>Reads one page of the media tree (root when <paramref name="parent"/> is null).</summary>
    /// <param name="parent">The parent to list beneath; null for the media root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's items and the level's total.</returns>
    private async Task<(
        IReadOnlyList<Gen.MediaTreeItemResponseModel> Items,
        int Total
    )> FetchMediaTreePageAsync(Guid? parent, int skip, int take, CancellationToken ct)
    {
        var paged = parent is null
            ? await _api.Umbraco.Management.Api.V1.Tree.Media.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            )
            : await _api.Umbraco.Management.Api.V1.Tree.Media.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        return (paged?.Items ?? [], (int)(paged?.Total ?? 0));
    }

    /// <summary>Reads a document tree item's id, name, and whether it has children.</summary>
    /// <param name="i">The generated document tree item.</param>
    /// <returns>The node's id, display name, and has-children flag.</returns>
    private static (Guid Id, string Name, bool HasChildren) ProjectDocument(
        Gen.DocumentTreeItemResponseModel i
    ) => (i.Id ?? Guid.Empty, DocumentName(i), i.HasChildren ?? false);

    /// <summary>Reads a media tree item's id, name, and whether it has children.</summary>
    /// <param name="i">The generated media tree item.</param>
    /// <returns>The node's id, display name, and has-children flag.</returns>
    private static (Guid Id, string Name, bool HasChildren) ProjectMedia(
        Gen.MediaTreeItemResponseModel i
    ) => (i.Id ?? Guid.Empty, MediaName(i), i.HasChildren ?? false);

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
                ? new ContentTypeRef { Id = dtId }
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
            MediaType = item.MediaType?.Id is { } mtId ? new ContentTypeRef { Id = mtId } : null,
            Parent = item.Parent?.Id is { } pId ? new ContentParentReference { Id = pId } : null,
        };

    /// <summary>Splits a <c>/</c>-separated name path into trimmed, non-empty segments.</summary>
    /// <param name="path">The path text.</param>
    /// <returns>The path segments in order.</returns>
    private static string[] SplitPath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
