namespace Umbraco.Cli.Client;

/// <summary>Webhook read, create, and delete, plus the events a webhook can subscribe to.</summary>
public interface IWebhookClient
{
    Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken ct = default
    );

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
}
