using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// Webhook event aliases (#234). Umbraco saves a webhook whose event name it does not know - it
/// shows up as <c>"eventType":"Other"</c> and never fires - so a create checks every requested
/// alias against <c>GET webhook/events</c> first and refuses the unknown ones. The nearest real
/// alias is suggested by the command layer, from the structured failure (#278).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Page size for the full event read. Umbraco ships a few dozen events, so one page normally
    /// holds them all; <see cref="KnownWebhookEventAliasesAsync"/> pages on if it does not.
    /// </summary>
    private const int WebhookEventPage = 1000;

    /// <summary>Lists the webhook events this instance can fire, via <c>GET webhook/events</c>.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of events; <see cref="WebhookEvent.Alias"/> is the value <c>webhook create --event</c> takes.</returns>
    public Task<UmbracoResponse<PagedResponse<WebhookEvent>>> GetWebhookEventsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Webhook.Events.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<WebhookEvent>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(e => new WebhookEvent
                        {
                            EventName = e.EventName ?? "",
                            EventType = e.EventType,
                            Alias = e.Alias,
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>
    /// Refuses a webhook create whose events include an alias the instance does not know. Matching
    /// is exact (ordinal): a near miss such as a wrong case is refused rather than silently
    /// corrected, so what is saved is always what was typed. The failure carries the unknown and
    /// known aliases, so the command layer can suggest the correct spelling (#278).
    /// </summary>
    /// <param name="events">The requested event aliases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every alias is known.</returns>
    /// <exception cref="InvalidArgumentException">An alias is blank (invalid_argument).</exception>
    /// <exception cref="UnknownValuesException">An alias is unknown (invalid_argument).</exception>
    /// <exception cref="ApiException">The event list could not be read (mapped to 400).</exception>
    private Task GuardWebhookEventsAsync(IEnumerable<string> events, CancellationToken ct) =>
        GuardKnownValuesAsync(
            events.ToList(),
            KnownWebhookEventAliasesAsync,
            "A webhook event alias cannot be empty.",
            "Could not read this instance's webhook events (GET webhook/events) to check the "
                + "--event aliases. Umbraco saves a webhook with an unknown event but never "
                + "fires it, so the request was not sent.",
            (unknown, _) =>
                $"Unknown webhook event {string.Join(", ", unknown.Select(u => $"'{u}'"))}. "
                + "Umbraco would save the webhook but never fire it.",
            ct
        );

    /// <summary>
    /// Every event alias the instance knows, read through <see cref="GetWebhookEventsAsync"/>, or
    /// null when the list could not be read. A failed read (a 403, a transport error) is reported by
    /// the guard as "could not check" rather than as the events endpoint's own error, which would
    /// read as the create itself failing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The distinct aliases; null on a failed or empty read.</returns>
    private async Task<HashSet<string>?> KnownWebhookEventAliasesAsync(CancellationToken ct)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        for (var skip = 0; ; skip += WebhookEventPage)
        {
            var page = await GetWebhookEventsAsync(skip, WebhookEventPage, ct);
            if (!page.IsSuccess)
                return null;

            var items = page.Data!.Items.ToList();
            aliases.UnionWith(
                items.Select(e => e.Alias).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!)
            );
            if (items.Count == 0 || skip + items.Count >= page.Data.Total)
                break;
        }

        // Umbraco always registers its core events, so an empty list means the read failed.
        return aliases.Count == 0 ? null : aliases;
    }
}
