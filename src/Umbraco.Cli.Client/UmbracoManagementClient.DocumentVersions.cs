using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Document version history on <see cref="UmbracoManagementClient"/> (issues #58, #209): list a
/// document's versions (across every culture when none is named), read one version raw, and roll
/// back to one.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Lists a document's version history via <c>GET document-version?documentId=</c>
    /// (generated client, issue #58).
    /// <para>
    /// Umbraco returns no versions for a document that varies by culture unless a culture is
    /// passed (#209), which read as "no history". So when no culture is named the document is
    /// read first: an invariant one gets the single unfiltered query, a variant one gets a query
    /// per culture and the results are merged newest first. Every row carries the culture it was
    /// listed under (null when invariant), which is also the <c>--culture</c> that
    /// <c>content version rollback</c> needs for that version.
    /// </para>
    /// </summary>
    /// <param name="documentId">The document whose versions to list.</param>
    /// <param name="culture">Culture to list versions for; null lists every culture the document has.</param>
    /// <param name="skip">Number of items to skip (paging, over the merged list).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of versions mapped to <see cref="DocumentVersionResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<DocumentVersionResponse>>> GetDocumentVersionsAsync(
        Guid documentId,
        string? culture = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var cultures = !string.IsNullOrEmpty(culture)
                    ? [culture]
                    : await DocumentCulturesAsync(documentId, ct);

                // One culture, or none (invariant: query unfiltered), pages on the server as before.
                if (cultures.Count <= 1)
                    return await VersionsPageAsync(
                        documentId,
                        cultures.FirstOrDefault(),
                        skip,
                        take,
                        ct
                    );

                // Several: the merged page can draw on the first skip+take rows of any culture,
                // so read that many of each, merge, then cut the requested page out of the merge.
                var pages = new List<PagedResponse<DocumentVersionResponse>>();
                foreach (var c in cultures)
                    pages.Add(await VersionsPageAsync(documentId, c, 0, skip + take, ct));

                return new PagedResponse<DocumentVersionResponse>
                {
                    Total = pages.Sum(p => p.Total),
                    Items = pages
                        .SelectMany(p => p.Items)
                        .NewestFirst()
                        .Skip(skip)
                        .Take(take)
                        .ToList(),
                };
            }
        );

    /// <summary>One culture's page of a document's versions, each row tagged with that culture.</summary>
    /// <param name="documentId">The document whose versions to list.</param>
    /// <param name="culture">The culture to filter by; null for an invariant document.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page.</returns>
    private async Task<PagedResponse<DocumentVersionResponse>> VersionsPageAsync(
        Guid documentId,
        string? culture,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        var paged = await _api.Umbraco.Management.Api.V1.DocumentVersion.GetAsync(
            c =>
            {
                c.QueryParameters.DocumentId = documentId;
                c.QueryParameters.Skip = skip;
                c.QueryParameters.Take = take;
                if (!string.IsNullOrEmpty(culture))
                    c.QueryParameters.Culture = culture;
            },
            ct
        );
        return new PagedResponse<DocumentVersionResponse>
        {
            Total = (int)(paged?.Total ?? 0),
            Items = (paged?.Items ?? [])
                .Select(v => new DocumentVersionResponse
                {
                    Id = v.Id ?? Guid.Empty,
                    Culture = culture,
                    VersionDate = v.VersionDate ?? default,
                    IsCurrentDraftVersion = v.IsCurrentDraftVersion ?? false,
                    IsCurrentPublishedVersion = v.IsCurrentPublishedVersion ?? false,
                    PreventCleanup = v.PreventCleanup ?? false,
                })
                .NewestFirst()
                .ToList(),
        };
    }

    /// <summary>
    /// Reads one version of a document, with its values, via <c>GET document-version/{id}</c>
    /// (#209). The body is returned raw so every property value survives, which is what a
    /// diff before <c>content version rollback</c> needs.
    /// </summary>
    /// <para>
    /// Like <c>content get</c> and <c>document-blueprint get</c>, it adds a top-level <c>name</c>
    /// (the first variant's) and the document's current <c>parent</c> (#315); the rest of the body
    /// is left exactly as Umbraco sent it.
    /// </para>
    /// <param name="versionId">The version id, from <see cref="GetDocumentVersionsAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version body as JSON, or a mapped failure.</returns>
    public Task<UmbracoResponse<JsonNode>> GetDocumentVersionAsync(
        Guid versionId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var version = await GetRawJsonAsync(
                    $"umbraco/management/api/v1/document-version/{versionId}",
                    ct
                );
                if (version is not JsonObject obj)
                    return version;

                if (
                    obj["name"] is null
                    && obj["variants"] is JsonArray { Count: > 0 } variants
                    && variants[0]?["name"] is JsonValue name
                )
                    obj["name"] = name.DeepClone();

                // A version has no placement of its own: the parent is the document's, read
                // best-effort from the tree as content get does.
                if (
                    obj["document"]?["id"]?.GetValue<string>() is { } raw
                    && Guid.TryParse(raw, out var documentId)
                    && await DocumentParentAsync(documentId, ct) is { } parent
                )
                    obj["parent"] = new JsonObject { ["id"] = parent.Id.ToString() };
                return obj;
            }
        );

    /// <inheritdoc />
    public async Task<UmbracoResponse<Guid>> GetVersionDocumentIdAsync(
        Guid versionId,
        CancellationToken ct = default
    )
    {
        var version = await GuardedApiAsync(
            ct,
            () =>
                _api
                    .Umbraco.Management.Api.V1.DocumentVersion[versionId]
                    .GetAsync(cancellationToken: ct)
        );
        if (!version.IsSuccess)
            return UmbracoResponse<Guid>.FailureFrom(version);

        // The server answered 200 without saying whose version it is: its answer, not the
        // request, is at fault, hence unexpected_response.
        return version.Data?.Document?.Id is { } id
            ? UmbracoResponse<Guid>.Success(id)
            : UmbracoResponse<Guid>.Failure(
                200,
                $"Umbraco returned version {versionId} without the document it belongs to.",
                FailureCategory.UnexpectedResponse
            );
    }

    /// <summary>
    /// Rolls a document back to a previous version via <c>POST document-version/{id}/rollback</c>
    /// (generated client, issue #58). The endpoint returns no body.
    /// </summary>
    /// <param name="versionId">The id of the version to roll back to.</param>
    /// <param name="culture">Culture to roll back; null for the invariant/default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> RollbackDocumentVersionAsync(
        Guid versionId,
        string? culture = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentVersion[versionId]
                    .Rollback.PostAsync(
                        c =>
                        {
                            if (!string.IsNullOrEmpty(culture))
                                c.QueryParameters.Culture = culture;
                        },
                        ct
                    );
                return Empty.Value;
            }
        );
}

/// <summary>The one order <c>content version list</c> shows versions in.</summary>
internal static class DocumentVersionOrdering
{
    /// <summary>
    /// Orders versions newest first. Publishing turns the draft into the published version and
    /// starts a new draft at the same moment, so the two share a <c>versionDate</c> (#294); the
    /// current draft goes first, then the current published version, so the order is stable
    /// instead of whatever order the server or the merge happened to produce.
    /// </summary>
    /// <param name="versions">The versions to order.</param>
    /// <returns>The versions, newest first.</returns>
    public static IEnumerable<DocumentVersionResponse> NewestFirst(
        this IEnumerable<DocumentVersionResponse> versions
    ) =>
        versions
            .OrderByDescending(v => v.VersionDate)
            .ThenByDescending(v => v.IsCurrentDraftVersion)
            .ThenByDescending(v => v.IsCurrentPublishedVersion);
}
