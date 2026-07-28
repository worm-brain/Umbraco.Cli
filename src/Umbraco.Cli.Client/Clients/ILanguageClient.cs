namespace Umbraco.Cli.Client;

/// <summary>Language read, create, and delete.</summary>
public interface ILanguageClient
{
    Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(
        CancellationToken ct = default
    );

    Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(
        CreateLanguageRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a language by ISO code (issue #59).</summary>
    /// <param name="isoCode">The ISO code of the language to update.</param>
    /// <param name="request">The replacement name/flags/fallback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated language, or a mapped failure.</returns>
    Task<UmbracoResponse<LanguageResponse>> UpdateLanguageAsync(
        string isoCode,
        UpdateLanguageRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteLanguageAsync(
        string isoCode,
        CancellationToken ct = default
    );
}
