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

    /// <summary>Deletes a dictionary item by id (issue #59).</summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteDictionaryItemAsync(Guid id, CancellationToken ct = default);
}
