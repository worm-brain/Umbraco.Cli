namespace Umbraco.Cli.Client;

/// <summary>A document type that uses a template (see <see cref="ITemplateClient.GetTemplateUsageAsync"/>).</summary>
/// <param name="DocumentTypeId">The document type id.</param>
/// <param name="Name">Its name, for messages.</param>
public sealed record TemplateUser(Guid DocumentTypeId, string Name);

/// <summary>Template read access.</summary>
public interface ITemplateClient
{
    Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0,
        int take = 20,
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

    /// <summary>
    /// Which document types use each template, by allowing it or defaulting to it (#269). Umbraco
    /// has no referenced-by endpoint for templates, so this reads every document type once, in
    /// batches, or one at a time on Umbraco 17.0-17.2, which has no batch endpoint (#432); a
    /// template no document type uses has no entry. Any read failing fails the whole call, so a
    /// guard never mistakes an unread type for one that does not use the template.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document types keyed by template id, or a mapped failure.</returns>
    Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>
    > GetTemplateUsageAsync(CancellationToken ct = default);
}
