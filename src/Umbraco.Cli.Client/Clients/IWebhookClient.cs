namespace Umbraco.Cli.Client;

/// <summary>Webhook read, create, and delete.</summary>
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
}
