namespace Umbraco.Cli.Client;

/// <summary>Dictionary item read and create.</summary>
public interface IDictionaryClient
{
    Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(
        string key,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(
        CreateDictionaryItemRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lists dictionary items from the tree (issue #110): the root level when no parent is given,
    /// or the direct children of <paramref name="parentId"/> when one is. Each row carries the
    /// parent id and whether it has children, so the hierarchy can be walked level by level.
    /// </summary>
    /// <param name="parentId">Parent id to list children of; null lists the root level.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of dictionary tree items, or a mapped failure.</returns>
    Task<UmbracoResponse<PagedResponse<DictionaryTreeItem>>> GetDictionaryTreeAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Reparents a dictionary item under a new target (issue #110).</summary>
    /// <param name="id">The dictionary item id to move.</param>
    /// <param name="targetId">Target parent id; null moves the item to the dictionary root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    /// <summary>
    /// Updates a dictionary item (#182), merging the supplied translations into the item's
    /// existing ones by ISO code. Correcting one language no longer means delete-and-recreate,
    /// which changed the item's id.
    /// </summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="request">The name and translations to write.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The item as the instance holds it afterwards, or a mapped failure.</returns>
    Task<UmbracoResponse<DictionaryItemResponse>> UpdateDictionaryItemAsync(
        Guid id,
        UpdateDictionaryItemRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> MoveDictionaryItemAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    );

    /// <summary>Deletes a dictionary item by id (issue #59).</summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDictionaryItemAsync(Guid id, CancellationToken ct = default);
}
