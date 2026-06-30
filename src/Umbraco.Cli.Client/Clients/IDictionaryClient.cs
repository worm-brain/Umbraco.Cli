namespace Umbraco.Cli.Client;

/// <summary>Dictionary item read and create.</summary>
public interface IDictionaryClient
{
    Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(string key, CancellationToken ct = default);

    Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(CreateDictionaryItemRequest request, CancellationToken ct = default);
}
