namespace Umbraco.Cli.Client;

/// <summary>Language read, create, and delete.</summary>
public interface ILanguageClient
{
    Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(CancellationToken ct = default);

    Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<Empty>> DeleteLanguageAsync(string isoCode, CancellationToken ct = default);
}
