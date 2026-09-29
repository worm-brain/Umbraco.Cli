using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Webhook get, update and delivery log (#237), plus resolving a webhook by name. List, create and
/// delete live with the rest of the original client; the event guard is in the WebhookEvents part.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    private List<ReferenceCandidate>? _webhookCandidates;

    /// <summary>Reads one webhook via <c>GET webhook/{id}</c>.</summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The webhook, mapped as <c>webhook list</c> maps it, or a mapped failure.</returns>
    public Task<UmbracoResponse<WebhookResponse>> GetWebhookAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
                MapWebhook(
                    await _api.Umbraco.Management.Api.V1.Webhook[id].GetAsync(cancellationToken: ct)
                        ?? throw NotFound($"No webhook with id {id}.")
                )
        );

    /// <summary>
    /// Updates a webhook via <c>PUT webhook/{id}</c>. The API takes the whole webhook and resets
    /// anything left out, so the current webhook is read first and the request laid over it
    /// (docs/conventions.md 5.1): scalars given replace, a given event or type list replaces the
    /// list, and headers merge by name (an empty value removes one); with <see cref="UpdateWebhookRequest.Replace"/> the given
    /// headers and types are the whole set, so omitting them clears them. New event aliases get the same check as on create (#234),
    /// so an update cannot subscribe a webhook to an event that never fires. The result is read
    /// back, so it is what <c>webhook get</c> would show.
    /// </summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="request">The fields to change; null members keep their values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The webhook as saved, or a mapped failure.</returns>
    public Task<UmbracoResponse<WebhookResponse>> UpdateWebhookAsync(
        Guid id,
        UpdateWebhookRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                if (request.Events is { } events)
                    await GuardWebhookEventsAsync(events, ct);

                var item = _api.Umbraco.Management.Api.V1.Webhook[id];
                var current = MapWebhook(
                    await item.GetAsync(cancellationToken: ct)
                        ?? throw NotFound($"No webhook with id {id}.")
                );

                await item.PutAsync(MergeWebhook(current, request), cancellationToken: ct);

                return MapWebhook(
                    await item.GetAsync(cancellationToken: ct)
                        ?? throw NotFound($"No webhook with id {id}.")
                );
            }
        );

    /// <summary>
    /// The <c>PUT</c> body for an update: each given value over the current one. Events are sent
    /// as aliases, which is what the API takes; the read model carries them as objects (#46).
    /// </summary>
    /// <param name="current">The webhook as it is now.</param>
    /// <param name="request">The fields to change.</param>
    /// <returns>The body to send.</returns>
    internal static Gen.UpdateWebhookRequestModel MergeWebhook(
        WebhookResponse current,
        UpdateWebhookRequest request
    )
    {
        // Header names are case-insensitive on the wire, so X-Token and x-token are one header:
        // a given one replaces the current one rather than sending both. A replace starts from
        // no headers, so the given set is the whole set. An empty value removes the header
        // (#367): an empty header is never useful to a receiver, and without this the only way
        // to drop one header was --replace with every other header (secrets included) retyped.
        var headers = new Dictionary<string, string>(
            request.Replace ? [] : current.Headers ?? [],
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
        {
            if (value.Length == 0)
                headers.Remove(name);
            else
                headers[name] = value;
        }

        var body = new Gen.UpdateWebhookRequestModel
        {
            Name = request.Name ?? current.Name,
            Description = request.Description ?? current.Description,
            Url = request.Url ?? current.Url,
            Enabled = request.Enabled ?? current.Enabled,
            Events =
                request.Events?.ToList()
                ?? (current.Events ?? [])
                    .Select(e => e.Alias)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .Select(a => a!)
                    .ToList(),
            // A replace with no types clears the filter: the webhook fires for every type.
            ContentTypeKeys = (
                request.ContentTypeKeys ?? (request.Replace ? [] : current.ContentTypeKeys ?? [])
            )
                .Select(k => (Guid?)k)
                .ToList(),
            // Always sent, even empty: the PUT replaces the whole webhook, headers included.
            Headers = new Gen.UpdateWebhookRequestModel_headers(),
        };
        foreach (var (name, value) in headers)
            body.Headers.AdditionalData[name] = value;
        return body;
    }

    /// <summary>
    /// Lists delivery attempts via <c>GET webhook/{id}/logs</c>, or <c>GET webhook/logs</c> for
    /// every webhook when <paramref name="webhookId"/> is null.
    /// </summary>
    /// <param name="webhookId">The webhook whose log to read; null for all webhooks.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of log entries, or a mapped failure.</returns>
    public Task<UmbracoResponse<PagedResponse<WebhookLog>>> GetWebhookLogsAsync(
        Guid? webhookId,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // The two endpoints share a response model but not a query-parameter type, so
                // each is configured on its own.
                var paged = webhookId is { } id
                    ? await _api
                        .Umbraco.Management.Api.V1.Webhook[id]
                        .Logs.GetAsync(
                            c =>
                            {
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        )
                    : await _api.Umbraco.Management.Api.V1.Webhook.Logs.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<WebhookLog>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapWebhookLog).ToList(),
                };
            }
        );

    /// <summary>Maps a generated log entry onto <see cref="WebhookLog"/>.</summary>
    /// <param name="log">The generated model.</param>
    /// <returns>The mapped entry.</returns>
    private static WebhookLog MapWebhookLog(Gen.WebhookLogResponseModel log) =>
        new()
        {
            Id = log.Key ?? Guid.Empty,
            WebhookId = log.WebhookKey ?? Guid.Empty,
            Date = log.Date,
            EventAlias = log.EventAlias,
            Url = log.Url,
            StatusCode = log.StatusCode,
            IsSuccessStatusCode = log.IsSuccessStatusCode ?? false,
            // Umbraco spells it "exceptionOccured"; the CLI's field is spelled correctly.
            ExceptionOccurred = log.ExceptionOccured ?? false,
            RetryCount = log.RetryCount ?? 0,
            RequestHeaders = log.RequestHeaders,
            RequestBody = log.RequestBody,
            ResponseHeaders = log.ResponseHeaders,
            ResponseBody = log.ResponseBody,
        };

    /// <summary>
    /// Resolves the values given to a webhook's <c>--type</c> filter (#237) to type ids. A GUID is
    /// taken as it is. Anything else is looked up as a document type alias, a media type alias or
    /// name, and a member type alias, and must name exactly one type across the three: the
    /// filter holds whichever kind the webhook's events are about, and does not say which.
    /// <para>
    /// An alias no kind has is refused with the known aliases attached, so the command layer can
    /// suggest the nearest one (#368). When <paramref name="events"/> is given, a filter that can
    /// never match them is refused too (#368): Umbraco applies the filter to the item an event is
    /// about, so a filter of only document types on media-only events means the webhook never
    /// fires. The check is skipped whenever it cannot be sure: a type given by id (its kind is not
    /// looked up), an event whose kind is not content, media or member, or an event list that
    /// cannot be read.
    /// </para>
    /// <para>
    /// This lives in the client rather than on top of <see cref="ResolveIdAsync"/> because telling
    /// "not this kind" (404) from "ambiguous within this kind" (409) needs the resolver's own
    /// exception; a failed <see cref="UmbracoResponse{T}"/> carries no status for either (#256).
    /// </para>
    /// </summary>
    /// <param name="references">Type ids or aliases, in the order given.</param>
    /// <param name="events">The event aliases the webhook will have, to check the filter against; null skips that check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids in the order given, or an invalid_argument failure naming the value.</returns>
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> ResolveWebhookTypesAsync(
        IEnumerable<string> references,
        IReadOnlyCollection<string>? events = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () =>
            {
                var resolved = new List<(Guid Id, string? Kind)>();
                foreach (var reference in references)
                    resolved.Add(await WebhookTypeAsync(reference, ct));

                if (resolved.Count > 0 && events is { Count: > 0 })
                    await GuardWebhookTypeKindsAsync(resolved, events, ct);
                return resolved.Select(r => r.Id).ToList();
            }
        );

    /// <summary>
    /// The webhook event type (<c>eventType</c> in <c>GET webhook/events</c>) whose items each type
    /// kind can match.
    /// </summary>
    private static readonly Dictionary<string, string> EventTypeByKind = new()
    {
        ["document type"] = "Content",
        ["media type"] = "Media",
        ["member type"] = "Member",
    };

    /// <summary>
    /// Refuses a type filter that none of the webhook's events can match; see
    /// <see cref="ResolveWebhookTypesAsync"/> for when the check is skipped.
    /// </summary>
    /// <param name="types">The resolved types, each with its kind (null when given by id).</param>
    /// <param name="events">The webhook's event aliases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when some event can match the filter, or the check is skipped.</returns>
    /// <exception cref="InvalidArgumentException">No event can match any of the types.</exception>
    private async Task GuardWebhookTypeKindsAsync(
        IReadOnlyList<(Guid Id, string? Kind)> types,
        IReadOnlyCollection<string> events,
        CancellationToken ct
    )
    {
        if (types.Any(t => t.Kind is null))
            return;

        var known = await ReadWebhookEventsAsync(ct);
        if (known is null)
            return;
        var eventTypes = events
            .Select(alias => known.FirstOrDefault(e => e.Alias == alias)?.EventType)
            .ToList();
        // An unknown alias is the event guard's to refuse; an "Other" event is not filtered by type.
        if (eventTypes.Any(t => t is null || !EventTypeByKind.ContainsValue(t)))
            return;

        var matchable = types.Select(t => EventTypeByKind[t.Kind!]).ToHashSet();
        if (eventTypes.Any(t => matchable.Contains(t!)))
            return;

        var kinds = string.Join(" and ", types.Select(t => t.Kind + "s").Distinct());
        var about = string.Join(" and ", eventTypes.Distinct().Select(t => t!.ToLowerInvariant()));
        throw new InvalidArgumentException(
            $"The --type filter holds only {kinds}, but the events are about {about}, so Umbraco "
                + "would save the webhook but never fire it. Use document types for content "
                + "events, media types for media events and member types for member events."
        );
    }

    /// <summary>One <c>--type</c> value's id and kind; see <see cref="ResolveWebhookTypesAsync"/>.</summary>
    /// <param name="reference">A type id or alias.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The type id, and its kind ("document type", "media type" or "member type"; null for an id).</returns>
    /// <exception cref="UnknownValuesException">No kind has the alias; carries every known alias.</exception>
    /// <exception cref="UnresolvedReferenceException">More than one type has the alias (409).</exception>
    private async Task<(Guid Id, string? Kind)> WebhookTypeAsync(
        string reference,
        CancellationToken ct
    )
    {
        if (Guid.TryParse(reference, out var id))
            return (id, null);

        (string Kind, Func<string, CancellationToken, Task<Guid>> Find)[] kinds =
        [
            ("document type", FindDocumentTypeIdAsync),
            ("media type", FindMediaTypeIdAsync),
            ("member type", FindMemberTypeIdAsync),
        ];
        var matches = new List<(string Kind, Guid Id)>();
        foreach (var (kind, find) in kinds)
        {
            try
            {
                matches.Add((kind, await find(reference, ct)));
            }
            catch (UnresolvedReferenceException e) when (e.ResponseStatusCode == 404)
            {
                // Not this kind; an ambiguous name (409) or a server error propagates.
            }
        }

        return matches switch
        {
            [var one] => (one.Id, one.Kind),
            // Each finder read every type of its kind before giving up, so the caches hold every
            // alias there is to suggest from.
            [] => throw new UnknownValuesException(
                $"No document type, media type or member type has the alias '{reference}'. Use "
                    + "'umbraco document-type list', 'media-type list' or 'member-type list' to "
                    + "find one, or pass its id.",
                new UnknownValues(
                    [reference],
                    [
                        .. _documentTypeAliasById.Values,
                        .. _mediaTypeAliasById.Values,
                        .. _memberTypeAliasById.Values,
                    ]
                )
            ),
            _ => throw new UnresolvedReferenceException(
                $"'{reference}' names more than one type: "
                    + string.Join(", ", matches.Select(m => $"{m.Id} ({m.Kind})"))
                    + ". Pass the id of the one you mean.",
                409
            ),
        };
    }

    /// <summary>
    /// Resolves a webhook name (#237). Webhooks have no alias, and the name is optional, so a
    /// webhook without one can only be named by its id. Every page of <c>GET webhook</c> is read
    /// once per client.
    /// </summary>
    /// <param name="reference">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The webhook id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindWebhookIdAsync(string reference, CancellationToken ct)
    {
        _webhookCandidates ??= await ReadAllPagesAsync(async (skip, take) => (
                    await _api.Umbraco.Management.Api.V1.Webhook.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                )?.Items?.Where(w => w.Id is not null).Select(w => new ReferenceCandidate(w.Id!.Value, null, w.Name)).ToList() ?? []);
        return ReferenceMatch.Pick(EntityKind.Webhook, reference, _webhookCandidates);
    }
}
