namespace Umbraco.Community.Cli.Client;

/// <summary>Language read, create, and delete.</summary>
public interface ILanguageClient
{
    /// <summary>Every language the site has (up to 1,000, in one request).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The languages, or a mapped failure.</returns>
    Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a language. Languages are keyed by ISO code and the endpoint returns no body, so the
    /// accepted request is returned as the created language.
    /// </summary>
    /// <param name="request">The language to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created language, or a mapped failure.</returns>
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

    /// <summary>Deletes the language with this ISO code.</summary>
    /// <param name="isoCode">The language's ISO code, e.g. <c>da-DK</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteLanguageAsync(
        string isoCode,
        CancellationToken ct = default
    );
}
