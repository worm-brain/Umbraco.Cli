using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Dictionary hierarchy support (issue #110): browsing the tree and reparenting items. Kept in its
/// own partial so these additions don't deepen the main client file; the dictionary CRUD stays with
/// the other list/get/create/delete verbs. Create-under-parent is a field on the existing create
/// request, so it lives with <c>CreateDictionaryItemAsync</c>.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Lists the dictionary tree via <c>tree/dictionary/root</c> or <c>tree/dictionary/children</c>
    /// (issue #110). There is no folder concept for dictionary items, so every row is a real item;
    /// each carries its parent id and whether it has children. Mirrors
    /// <see cref="GetDocumentBlueprintsAsync"/>.
    /// </summary>
    /// <param name="parentId">Parent id to list children of; null lists the root level.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of dictionary tree items, or a mapped failure.</returns>
    public Task<UmbracoResponse<PagedResponse<DictionaryTreeItem>>> GetDictionaryTreeAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var tree = _api.Umbraco.Management.Api.V1.Tree.Dictionary;
                var paged = parentId is { } pid
                    ? await tree.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = pid;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await tree.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<DictionaryTreeItem>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DictionaryTreeItem
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            HasChildren = i.HasChildren ?? false,
                            Parent = i.Parent?.Id is { } pId
                                ? new ContentParentReference { Id = pId }
                                : null,
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>
    /// Reparents a dictionary item via <c>PUT dictionary/{id}/move</c> (issue #110). A null target
    /// moves the item to the dictionary root. Mirrors <see cref="MoveDocumentBlueprintAsync"/>.
    /// </summary>
    /// <param name="id">The dictionary item id to move.</param>
    /// <param name="targetId">Target parent id; null moves the item to the dictionary root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> MoveDictionaryItemAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .Move.PutAsync(
                        new Gen.MoveDictionaryRequestModel
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
}
