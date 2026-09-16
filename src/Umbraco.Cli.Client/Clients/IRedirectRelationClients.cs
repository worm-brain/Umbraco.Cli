namespace Umbraco.Cli.Client;

/// <summary>URL redirect management (issue #118): list/inspect, delete, and the tracking toggle.</summary>
public interface IRedirectClient
{
    /// <summary>Lists tracked redirects, optionally filtered by a search term.</summary>
    /// <param name="filter">A text filter over the redirect URLs; null for all.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of redirects.</returns>
    Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsAsync(
        string? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Lists the redirects that point at a given document (content key).</summary>
    /// <param name="contentKey">The destination document's id.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of redirects for the document.</returns>
    Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsForContentAsync(
        Guid contentKey,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets the current URL-tracking status.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tracking status.</returns>
    Task<UmbracoResponse<RedirectStatusResponse>> GetRedirectStatusAsync(
        CancellationToken ct = default
    );

    /// <summary>Deletes a redirect by id.</summary>
    /// <param name="id">The redirect id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteRedirectAsync(Guid id, CancellationToken ct = default);

    /// <summary>Enables or disables automatic URL-redirect tracking (site-wide).</summary>
    /// <param name="enabled">True to enable tracking; false to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> SetRedirectTrackingAsync(
        bool enabled,
        CancellationToken ct = default
    );
}

/// <summary>Read-only relation-type listing (issue #118).</summary>
public interface IRelationTypeClient
{
    /// <summary>Lists relation types.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of relation types.</returns>
    Task<UmbracoResponse<PagedResponse<RelationTypeResponse>>> GetRelationTypesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a relation type by id.</summary>
    /// <param name="id">The relation type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The relation type.</returns>
    Task<UmbracoResponse<RelationTypeResponse>> GetRelationTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );
}

/// <summary>Read-only relation listing (issue #118). Relations are listed by relation-type id.</summary>
public interface IRelationClient
{
    /// <summary>Lists the relations of a given relation type.</summary>
    /// <param name="relationTypeId">The relation type id.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of relations.</returns>
    Task<UmbracoResponse<PagedResponse<RelationResponse>>> GetRelationsByTypeAsync(
        Guid relationTypeId,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );
}
