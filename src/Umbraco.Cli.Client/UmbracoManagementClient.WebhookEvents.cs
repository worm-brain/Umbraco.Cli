using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// Webhook event aliases (#234). Umbraco saves a webhook whose event name it does not know - it
/// shows up as <c>"eventType":"Other"</c> and never fires - so a create checks every requested
/// alias against <c>GET webhook/events</c> first and refuses the unknown ones, naming the nearest
/// real alias.
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
    /// is exact (ordinal): a near miss such as a wrong case is refused with the correct spelling
    /// suggested rather than silently corrected, so what is saved is always what was typed.
    /// </summary>
    /// <param name="events">The requested event aliases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ApiException">
    /// An alias is blank or unknown, or the event list could not be read (all mapped to 400).
    /// </exception>
    private async Task GuardWebhookEventsAsync(IEnumerable<string> events, CancellationToken ct)
    {
        var requested = events.ToList();
        if (requested.Count == 0)
            return;

        if (requested.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("A webhook event alias cannot be empty.");

        var known = await KnownWebhookEventAliasesAsync(ct);

        // An unreadable list is a failure to validate, not a pass: letting the request through
        // would restore the saved-but-never-fires behaviour this guard exists to stop.
        if (known is null)
            throw BadRequest(
                "Could not read this instance's webhook events (GET webhook/events) to check the "
                    + "--event aliases. Umbraco saves a webhook with an unknown event but never "
                    + "fires it, so the request was not sent."
            );

        var unknown = requested.Where(e => !known.Contains(e)).ToList();
        if (unknown.Count == 0)
            return;

        var described = unknown.Select(u =>
            NearestWebhookEvent(u, [.. known]) is { } nearest
                ? $"'{u}' (did you mean '{nearest}'?)"
                : $"'{u}'"
        );
        throw BadRequest(
            $"Unknown webhook event {string.Join(", ", described)}. Umbraco would save the webhook "
                + "but never fire it. Run 'umbraco webhook event list' for the valid aliases."
        );
    }

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

    /// <summary>
    /// The known alias closest to <paramref name="typed"/>, or null when nothing is close. Distance
    /// is case-insensitive and also measured against the alias without its <c>Umbraco.</c> prefix,
    /// so the bare names people type (<c>ContentPublished</c>, <c>contentpublish</c>) still find
    /// <c>Umbraco.ContentPublish</c>.
    /// </summary>
    /// <param name="typed">The unknown alias as typed.</param>
    /// <param name="known">The instance's aliases.</param>
    /// <returns>The suggestion, or null.</returns>
    internal static string? NearestWebhookEvent(string typed, IReadOnlyList<string> known)
    {
        const string prefix = "Umbraco.";
        var needle = typed.ToLowerInvariant();

        // Close enough to be a typo or a tense slip, not a different event: a third of the typed
        // length, with a floor so short names still get a suggestion.
        var threshold = Math.Max(2, typed.Length / 3);

        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var alias in known)
        {
            var lower = alias.ToLowerInvariant();
            var distance = EditDistance(needle, lower);
            if (lower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                distance = Math.Min(distance, EditDistance(needle, lower[prefix.Length..]));
            if (distance < bestDistance)
                (best, bestDistance) = (alias, distance);
        }

        return bestDistance <= threshold ? best : null;
    }

    /// <summary>Levenshtein distance between two strings (insert, delete, substitute all cost 1).</summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The number of single-character edits that turn <paramref name="a"/> into <paramref name="b"/>.</returns>
    private static int EditDistance(string a, string b)
    {
        // Two-row dynamic programme: row i holds the distances from a[..i] to every prefix of b.
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
