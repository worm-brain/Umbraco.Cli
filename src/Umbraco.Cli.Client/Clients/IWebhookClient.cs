namespace Umbraco.Cli.Client;

/// <summary>
/// Webhook read, create, update and delete, the events a webhook can subscribe to, and the
/// delivery log.
/// </summary>
public interface IWebhookClient
{
    /// <summary>Lists webhooks, one page at a time.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of webhooks.</returns>
    Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Reads one webhook by id (<c>GET webhook/{id}</c>, #237).</summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The webhook, in the same shape as <c>webhook list</c>.</returns>
    Task<UmbracoResponse<WebhookResponse>> GetWebhookAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a webhook and returns it as the instance saved it.</summary>
    /// <param name="request">The webhook to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created webhook.</returns>
    Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Updates a webhook (<c>PUT webhook/{id}</c>, #237): reads it, lays the given fields over
    /// it, writes it back and returns it as <c>webhook get</c> shows it.
    /// </summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="request">The fields to change; null members keep their values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated webhook.</returns>
    Task<UmbracoResponse<WebhookResponse>> UpdateWebhookAsync(
        Guid id,
        UpdateWebhookRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a webhook.</summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or the failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteWebhookAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists the webhook events the instance can fire (<c>GET webhook/events</c>, #234).</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of events; each alias is a valid <c>webhook create --event</c> value.</returns>
    Task<UmbracoResponse<PagedResponse<WebhookEvent>>> GetWebhookEventsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves the values of a webhook's type filter (#237): each a GUID, or the alias of exactly
    /// one document, media or member type. An unknown alias fails carrying the known aliases
    /// (<see cref="UmbracoResponse{T}.UnknownValues"/>), and with <paramref name="events"/> a
    /// filter none of those events can match is refused (#368).
    /// </summary>
    /// <param name="references">Type ids or aliases.</param>
    /// <param name="events">The webhook's event aliases, to check the filter against; null skips that check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids in the order given, or an invalid_argument failure.</returns>
    Task<UmbracoResponse<IReadOnlyList<Guid>>> ResolveWebhookTypesAsync(
        IEnumerable<string> references,
        IReadOnlyCollection<string>? events = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lists delivery attempts (#237): one webhook's (<c>GET webhook/{id}/logs</c>) or, with no
    /// id, every webhook's (<c>GET webhook/logs</c>).
    /// </summary>
    /// <param name="webhookId">The webhook whose log to read; null for all webhooks.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of log entries.</returns>
    Task<UmbracoResponse<PagedResponse<WebhookLog>>> GetWebhookLogsAsync(
        Guid? webhookId,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );
}
