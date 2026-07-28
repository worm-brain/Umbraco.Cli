namespace Umbraco.Cli.Client;

/// <summary>Template read access.</summary>
public interface ITemplateClient
{
    Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(
        string alias,
        CancellationToken ct = default
    );

    /// <summary>Creates a template (issue #59).</summary>
    /// <param name="request">The template to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template (with the generated id), or a mapped failure.</returns>
    Task<UmbracoResponse<TemplateResponse>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a template by id (issue #59).</summary>
    /// <param name="id">The template id.</param>
    /// <param name="request">The replacement name/alias/content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateTemplateAsync(
        Guid id,
        UpdateTemplateRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a template by id (issue #59).</summary>
    /// <param name="id">The template id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}
