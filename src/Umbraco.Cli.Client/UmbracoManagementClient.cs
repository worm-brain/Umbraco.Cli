using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Umbraco.Cli.Client.Generated;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Production implementation of <see cref="IUmbracoManagementClient"/>.
///
/// Endpoints are driven by the Kiota client generated from the instance's
/// OpenAPI document (<c>src/Umbraco.Cli.Client/Generated</c>, regenerated via
/// <c>scripts/regen-client.ps1</c> — see GitHub issue #50). This class is a thin
/// adapter over that generated client: it keeps the <see cref="UmbracoResponse{T}"/>
/// envelope, the transport-failure guard, and the command-facing DTOs stable, so
/// commands never see the generated types or Kiota's exception-based failure model.
///
/// Migration is incremental (#50). Most calls now go through the generated request
/// builders via <see cref="GuardedApiAsync{T}"/>: all reads that had a clean generated
/// equivalent, every delete, the language/dictionary/webhook creates, and user invite.
/// Still on the hand-written <see cref="HttpClient"/> helpers below (tracked by #79, which
/// removes <see cref="_http"/> entirely): the content write path (create/update/publish/
/// unpublish) and media upload — they need document-type alias→id resolution, a
/// JSON→UntypedNode value converter, and the two-step temporary-file upload flow — plus the
/// handful of overlooked reads (document-type/data-type/user/dictionary/webhook) that are
/// mopped up there since <see cref="_http"/> lives on for the content/media writes anyway.
/// </summary>
public sealed class UmbracoManagementClient : IUmbracoManagementClient
{
    private readonly HttpClient _http;

    /// <summary>The Kiota-generated Management API client, backed by <see cref="_http"/>.</summary>
    private readonly UmbracoApiClient _api;

    /// <summary>
    /// The Kiota request adapter backing <see cref="_api"/>. Retained because a
    /// <see cref="MultipartBody"/> (used for the temporary-file upload) needs an adapter to
    /// resolve its per-part serializers, and the builder's own adapter is not publicly exposed.
    /// </summary>
    private readonly IRequestAdapter _adapter;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates the client over an already-configured <see cref="HttpClient"/> (base
    /// address + bearer header are applied upstream by the CLI's context factory).
    /// The Kiota request adapter reuses that same <see cref="HttpClient"/> and uses an
    /// anonymous auth provider, so the caller-supplied Authorization header is what
    /// authenticates every generated call — no auth wiring is duplicated here.
    /// </summary>
    /// <param name="http">The configured HTTP client (base address, auth header, timeout).</param>
    public UmbracoManagementClient(HttpClient http)
    {
        _http = http;

        // Kiota resolves "{+baseurl}" against the adapter's BaseUrl. The HttpClient's
        // base address is the host root with a trailing slash (e.g.
        // "https://host:45000/"); Kiota expects it without the trailing slash because
        // its URL templates already start with "/umbraco/...".
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: http
        );
        if (http.BaseAddress is not null)
            adapter.BaseUrl = http.BaseAddress.ToString().TrimEnd('/');

        _adapter = adapter;
        _api = new UmbracoApiClient(adapter);
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the authenticated identity via <c>GET user/current</c> (issue #40 — the
    /// previous <c>security/back-office/user-data</c> endpoint 404s on Umbraco 14+).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current user mapped to <see cref="CurrentUserResponse"/>.</returns>
    public Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var u = await _api.Umbraco.Management.Api.V1.User.Current.GetAsync(
                    cancellationToken: ct
                );
                return new CurrentUserResponse
                {
                    Id = u?.Id ?? Guid.Empty,
                    Email = u?.Email ?? "",
                    Name = u?.Name ?? "",
                    UserName = u?.UserName ?? "",
                };
            }
        );

    // ── Content ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists documents from the content tree (issue #39 — Umbraco 14+ has no flat
    /// <c>/document</c> collection endpoint). With no <paramref name="parentId"/> it reads
    /// <c>tree/document/root</c>; with one it reads <c>tree/document/children</c>. Tree
    /// items carry the display name under <c>variants[]</c>, which is flattened into the
    /// command-facing <see cref="ContentItemResponse.Name"/>.
    /// </summary>
    /// <param name="parentId">Parent document id to list children of; null for root.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of documents mapped to <see cref="ContentItemResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = parentId is null
                    ? await _api.Umbraco.Management.Api.V1.Tree.Document.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await _api.Umbraco.Management.Api.V1.Tree.Document.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = parentId;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<ContentItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapDocumentTreeItem).ToList(),
                };
            }
        );

    /// <summary>
    /// Gets a single document by id (issue #42 — the display name and dates live under
    /// <c>variants[]</c>, not at the top level, so a naive DTO returned an empty name and
    /// <c>0001-01-01</c> dates). Reads <c>GET /document/{id}</c> and flattens the invariant
    /// (or first) variant.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document mapped to <see cref="ContentItemResponse"/>.</returns>
    public Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var d = await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .GetAsync(cancellationToken: ct);
                var variant = (d?.Variants ?? []).FirstOrDefault();
                return new ContentItemResponse
                {
                    Id = d?.Id ?? id,
                    Name = variant?.Name ?? "",
                    ContentType = d?.DocumentType?.Id is { } dtId
                        ? new ContentTypeReference { Id = dtId }
                        : null,
                    IsPublished = (d?.Variants ?? []).Any(v =>
                        v.State
                            is Gen.DocumentVariantStateModel.Published
                                or Gen.DocumentVariantStateModel.PublishedPendingChanges
                    ),
                    CreateDate = variant?.CreateDate ?? default,
                    UpdateDate = variant?.UpdateDate ?? default,
                };
            }
        );

    /// <summary>
    /// Resolves a document-type reference - an alias (e.g. <c>textPage</c>) or a GUID id - to
    /// its id, which is what the generated create model requires. A value that parses as a GUID
    /// is used directly. Otherwise the alias is resolved via the document-type item search;
    /// because the search result model carries only name/id (not alias), each candidate's full
    /// document type is fetched and its alias compared exactly. This mirrors the proven template
    /// alias resolver (<see cref="GetTemplateByAliasAsync"/>), which relies on the same item
    /// search indexing aliases - with the one extra by-id fetch the doc-type item model forces.
    /// See ADR 0004.
    /// </summary>
    /// <param name="aliasOrId">The document-type alias or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved document-type id.</returns>
    /// <exception cref="ApiException">No document type matches the alias (mapped to a 404).</exception>
    private async Task<Guid> ResolveDocumentTypeIdAsync(string aliasOrId, CancellationToken ct)
    {
        if (Guid.TryParse(aliasOrId, out var parsed))
            return parsed;

        var search = await _api.Umbraco.Management.Api.V1.Item.DocumentType.Search.GetAsync(
            c =>
            {
                c.QueryParameters.Query = aliasOrId;
                c.QueryParameters.Take = 100;
            },
            ct
        );
        foreach (var item in search?.Items ?? [])
        {
            if (item.Id is not { } candidateId)
                continue;
            // The search item omits Alias, so read the full document type to compare it.
            var dt = await _api
                .Umbraco.Management.Api.V1.DocumentType[candidateId]
                .GetAsync(cancellationToken: ct);
            if (string.Equals(dt?.Alias, aliasOrId, StringComparison.OrdinalIgnoreCase))
                return candidateId;
        }

        throw NotFound($"No document type found with alias '{aliasOrId}'.");
    }

    /// <summary>
    /// Creates a content item via <c>POST document</c> (generated client, #79). The document
    /// type is passed by alias, which is resolved to an id first (the generated model references
    /// the type by id only). The id is client-generated so the new item's id is known despite
    /// the empty <c>201</c> body, and the item is re-read afterwards so the returned payload is
    /// fully hydrated (name/url), closing the content half of #74. A failed hydration read still
    /// returns success with the id - the create itself succeeded.
    /// </summary>
    /// <param name="request">The content to create (document type by alias, variants, values).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created content item, hydrated where possible, or a mapped failure.</returns>
    public Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(
        CreateContentRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // A GUID on the request is used directly; otherwise resolve the alias to an id.
                var reference =
                    request.ContentType.Id != Guid.Empty
                        ? request.ContentType.Id.ToString()
                        : request.ContentType.Alias;
                var documentTypeId = await ResolveDocumentTypeIdAsync(reference, ct);

                var id = Guid.NewGuid();
                var body = new Gen.CreateDocumentRequestModel
                {
                    Id = id,
                    DocumentType = new Gen.ReferenceByIdModel { Id = documentTypeId },
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                    Variants = request
                        .Variants.Select(v => new Gen.DocumentVariantRequestModel
                        {
                            Name = v.Name,
                            Culture = v.Culture,
                            Segment = v.Segment,
                        })
                        .ToList(),
                    Values = request
                        .Values.Select(cv => new Gen.DocumentValueModel
                        {
                            Alias = cv.Alias,
                            Culture = cv.Culture,
                            Segment = cv.Segment,
                            Value = UntypedNodeFactory.FromValue(cv.Value),
                        })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.Document.PostAsync(
                    body,
                    cancellationToken: ct
                );

                // Best-effort hydration (#74): re-read so name/url are populated, not just the id.
                var hydrated = await GetContentByIdAsync(id, ct);
                if (hydrated.IsSuccess && hydrated.Data is { } data)
                    return data;
                return new ContentItemResponse
                {
                    Id = id,
                    Name = request.Variants.FirstOrDefault()?.Name ?? "",
                };
            }
        );

    /// <summary>
    /// Updates a content item via <c>PUT document/{id}</c> (generated client, #79). The PUT
    /// replaces the item's variants and property values, then the item is re-read so the
    /// returned payload is hydrated (#74). A failed hydration read still returns success.
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="request">The variants and property values to write.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated content item, hydrated where possible, or a mapped failure.</returns>
    public Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.UpdateDocumentRequestModel
                {
                    Variants = request
                        .Variants.Select(v => new Gen.DocumentVariantRequestModel
                        {
                            Name = v.Name,
                            Culture = v.Culture,
                            Segment = v.Segment,
                        })
                        .ToList(),
                    Values = request
                        .Values.Select(cv => new Gen.DocumentValueModel
                        {
                            Alias = cv.Alias,
                            Culture = cv.Culture,
                            Segment = cv.Segment,
                            Value = UntypedNodeFactory.FromValue(cv.Value),
                        })
                        .ToList(),
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .PutAsync(body, cancellationToken: ct);

                var hydrated = await GetContentByIdAsync(id, ct);
                if (hydrated.IsSuccess && hydrated.Data is { } data)
                    return data;
                return new ContentItemResponse { Id = id };
            }
        );

    /// <summary>Deletes a content item via <c>DELETE document/{id}</c> (generated client).</summary>
    /// <param name="id">The content item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteContentAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Publishes a content item via <c>PUT document/{id}/publish</c> (generated client, #79).
    /// Each culture is sent as a publish schedule with no scheduled time (publish now); the
    /// default <c>"*"</c> publishes all cultures (Umbraco's wildcard - there is no dedicated
    /// enum for it).
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes all cultures (<c>"*"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.PublishDocumentRequestModel
                {
                    PublishSchedules = (cultures ?? ["*"])
                        .Select(c => new Gen.CultureAndScheduleRequestModel
                        {
                            Culture = c,
                            // No PublishTime/UnpublishTime = publish immediately.
                            Schedule = new Gen.ScheduleRequestModel(),
                        })
                        .ToList(),
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Publish.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Unpublishes a content item via <c>PUT document/{id}/unpublish</c> (generated client, #79).
    /// The unpublish payload is a plain list of cultures (distinct from publish's schedule list);
    /// the default <c>"*"</c> unpublishes all cultures.
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">Cultures to unpublish; null/empty unpublishes all cultures (<c>"*"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.UnpublishDocumentRequestModel
                {
                    Cultures = (cultures ?? ["*"]).ToList(),
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Unpublish.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Document Versions ──────────────────────────────────────────────────────

    /// <summary>
    /// Lists a document's version history via <c>GET document-version?documentId=</c>
    /// (generated client, issue #58).
    /// </summary>
    /// <param name="documentId">The document whose versions to list.</param>
    /// <param name="culture">Culture to filter versions by; null for the invariant/default.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
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
                            VersionDate = v.VersionDate ?? default,
                            IsCurrentDraftVersion = v.IsCurrentDraftVersion ?? false,
                            IsCurrentPublishedVersion = v.IsCurrentPublishedVersion ?? false,
                            PreventCleanup = v.PreventCleanup ?? false,
                        })
                        .ToList(),
                };
            }
        );

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

    // ── Content workflow (recycle bin, move/copy, publish descendants) ─────────

    /// <summary>Moves a document to the recycle bin via <c>PUT document/{id}/move-to-recycle-bin</c> (issue #67).</summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> TrashContentAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .MoveToRecycleBin.PutAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Restores a document from the recycle bin via <c>PUT recycle-bin/document/{id}/restore</c>
    /// (issue #67). The target parent is optional; null restores to the content root.
    /// </summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="parentId">Target parent to restore under; null restores to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.MoveMediaRequestModel
                {
                    Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                };
                await _api
                    .Umbraco.Management.Api.V1.RecycleBin.Document[id]
                    .Restore.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Empties the content recycle bin via <c>DELETE recycle-bin/document</c> (issue #67). Irreversible.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> EmptyContentRecycleBinAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.RecycleBin.Document.DeleteAsync(
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <summary>
    /// Moves a document under a new parent via <c>PUT document/{id}/move</c> (issue #67). A null
    /// parent moves the document to the content root.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="parentId">Target parent id; null moves to the content root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> MoveContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.MoveDocumentRequestModel
                {
                    Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Move.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Copies a document under a new parent via <c>POST document/{id}/copy</c> (issue #67).</summary>
    /// <param name="id">The document id to copy.</param>
    /// <param name="parentId">Target parent id; null copies to the content root.</param>
    /// <param name="includeDescendants">Whether to copy descendants too.</param>
    /// <param name="relateToOriginal">Whether to create a relation to the original.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.CopyDocumentRequestModel
                {
                    Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                    IncludeDescendants = includeDescendants,
                    RelateToOriginal = relateToOriginal,
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Copy.PostAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Publishes a document and its descendants via
    /// <c>PUT document/{id}/publish-with-descendants</c> (issue #67).
    /// </summary>
    /// <param name="id">The root document id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes all (<c>*</c>).</param>
    /// <param name="includeUnpublishedDescendants">Whether to also publish never-published descendants.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.PublishDocumentWithDescendantsRequestModel
                {
                    Cultures = (cultures ?? ["*"]).ToList(),
                    IncludeUnpublishedDescendants = includeUnpublishedDescendants,
                };
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .PublishWithDescendants.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Media ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists media from the media tree (issue #39 — no flat <c>/media</c> collection on
    /// Umbraco 14+). Reads <c>tree/media/root</c> or <c>tree/media/children</c>; the
    /// display name comes from <c>variants[]</c>.
    /// </summary>
    /// <param name="parentId">Parent media id to list children of; null for root.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of media mapped to <see cref="MediaItemResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = parentId is null
                    ? await _api.Umbraco.Management.Api.V1.Tree.Media.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await _api.Umbraco.Management.Api.V1.Tree.Media.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = parentId;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<MediaItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapMediaTreeItem).ToList(),
                };
            }
        );

    /// <summary>
    /// Gets a single media item by id (issue #42 — name/dates come from <c>variants[]</c>).
    /// Reads <c>GET /media/{id}</c>.
    /// </summary>
    /// <param name="id">The media id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The media item mapped to <see cref="MediaItemResponse"/>.</returns>
    public Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var m = await _api
                    .Umbraco.Management.Api.V1.Media[id]
                    .GetAsync(cancellationToken: ct);
                var variant = (m?.Variants ?? []).FirstOrDefault();
                return new MediaItemResponse
                {
                    Id = m?.Id ?? id,
                    Name = variant?.Name ?? "",
                    MediaType = m?.MediaType?.Id is { } mtId
                        ? new ContentTypeReference { Id = mtId }
                        : null,
                    CreateDate = variant?.CreateDate ?? default,
                    UpdateDate = variant?.UpdateDate ?? default,
                };
            }
        );

    /// <summary>
    /// Resolves a media-type reference - a name (e.g. <c>Image</c>) or a GUID id - to its id.
    /// A value that parses as a GUID is used directly; otherwise it is treated as a media-type
    /// name and matched (case-insensitively) against the media-type item search. Media types are
    /// addressed by name here (not alias) because that is the established contract and the search
    /// item model exposes the name directly (Umbraco's built-in media types are "Image", "File",
    /// "Folder", ...).
    /// </summary>
    /// <param name="mediaType">The media-type name or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved media-type id.</returns>
    /// <exception cref="ApiException">No media type matches the name (mapped to a 404).</exception>
    private async Task<Guid> ResolveMediaTypeIdAsync(string mediaType, CancellationToken ct)
    {
        if (Guid.TryParse(mediaType, out var parsed))
            return parsed;

        var search = await _api.Umbraco.Management.Api.V1.Item.MediaType.Search.GetAsync(
            c =>
            {
                c.QueryParameters.Query = mediaType;
                c.QueryParameters.Take = 100;
            },
            ct
        );
        var match = (search?.Items ?? []).FirstOrDefault(m =>
            string.Equals(m.Name, mediaType, StringComparison.OrdinalIgnoreCase)
        );
        if (match?.Id is not { } id)
            throw NotFound(
                $"No media type found with the name '{mediaType}'. Use 'umbraco media-types list' "
                    + "to find one, or pass a media type id."
            );
        return id;
    }

    /// <summary>
    /// Uploads a file as a media item via the Umbraco 14+ two-step flow (generated client, #79):
    /// stage the bytes to <c>temporary-file</c> (multipart), then create the media item as JSON
    /// referencing that staged file's id in the <c>umbracoFile</c> property value. Staging
    /// decouples the (potentially large) byte transfer from the media create. The media id is
    /// client-generated so it is known despite the empty create response.
    /// </summary>
    /// <remarks>
    /// Under <c>--dry-run</c> the mutation interceptor fakes the temporary-file staging without
    /// forwarding it (so nothing is staged) and previews the media-create POST instead - the
    /// meaningful operation. This client method is unaware of the dry-run policy; see the
    /// interceptor and ADR 0004.
    /// </remarks>
    /// <param name="parentId">Parent media folder id; null/<see cref="Guid.Empty"/> for the media root.</param>
    /// <param name="name">Display name for the new media item.</param>
    /// <param name="fileStream">The file contents to upload.</param>
    /// <param name="fileName">The original file name (used for the staged file part).</param>
    /// <param name="contentType">The file's MIME type.</param>
    /// <param name="mediaType">The media type to create the item as: a media type id (GUID) or name (e.g. "Image").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media item (id + echoed name), or a mapped failure.</returns>
    public Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid? parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        string mediaType,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Step 1: resolve the media type to an id (GUID passthrough, else name search).
                var mediaTypeId = await ResolveMediaTypeIdAsync(mediaType, ct);

                // Step 2: stage the file bytes to the temporary-file endpoint (multipart form with
                // a client-generated "Id" part and the "File" part). The Kiota MultipartBody needs
                // the request adapter to resolve the per-part serializers.
                var temporaryFileId = Guid.NewGuid();
                var multipart = new MultipartBody { RequestAdapter = _adapter };
                multipart.AddOrReplacePart("Id", "text/plain", temporaryFileId.ToString());
                multipart.AddOrReplacePart("File", contentType, fileStream, fileName);
                await _api.Umbraco.Management.Api.V1.TemporaryFile.PostAsync(
                    multipart,
                    cancellationToken: ct
                );

                // Step 3: create the media item, pointing umbracoFile at the staged temp file. The
                // value shape ({ temporaryFileId }) maps to an UntypedNode like any property value.
                var mediaId = Guid.NewGuid();
                var body = new Gen.CreateMediaRequestModel
                {
                    Id = mediaId,
                    MediaType = new Gen.ReferenceByIdModel { Id = mediaTypeId },
                    Parent = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                    Variants = [new Gen.MediaVariantRequestModel { Name = name }],
                    Values =
                    [
                        new Gen.MediaValueModel
                        {
                            Alias = "umbracoFile",
                            Value = UntypedNodeFactory.FromValue(new { temporaryFileId }),
                        },
                    ],
                };
                await _api.Umbraco.Management.Api.V1.Media.PostAsync(body, cancellationToken: ct);

                // The create response is empty; the id is the client-generated one and the name is
                // echoed so the command reports a populated item rather than a blank one (#74).
                return new MediaItemResponse { Id = mediaId, Name = name };
            }
        );

    /// <summary>Deletes a media item via <c>DELETE media/{id}</c> (generated client).</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteMediaAsync(Guid id, CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.Media[id].DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Moves a media item to the recycle bin via <c>PUT media/{id}/move-to-recycle-bin</c> (issue #67).</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> TrashMediaAsync(Guid id, CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Media[id]
                    .MoveToRecycleBin.PutAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Restores a media item from the recycle bin via <c>PUT recycle-bin/media/{id}/restore</c>
    /// (issue #67). A null parent restores to the media root.
    /// </summary>
    /// <param name="id">The trashed media item id.</param>
    /// <param name="parentId">Target parent to restore under; null restores to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> RestoreMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.MoveMediaRequestModel
                {
                    Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                };
                await _api
                    .Umbraco.Management.Api.V1.RecycleBin.Media[id]
                    .Restore.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Empties the media recycle bin via <c>DELETE recycle-bin/media</c> (issue #67). Irreversible.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> EmptyMediaRecycleBinAsync(CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.RecycleBin.Media.DeleteAsync(
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <summary>
    /// Moves a media item under a new parent folder via <c>PUT media/{id}/move</c> (issue #67). A
    /// null parent moves the item to the media root.
    /// </summary>
    /// <param name="id">The media item id.</param>
    /// <param name="parentId">Target parent folder id; null moves to the media root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> MoveMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.MoveMediaRequestModel
                {
                    Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                };
                await _api
                    .Umbraco.Management.Api.V1.Media[id]
                    .Move.PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Media Types ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists media types from <c>tree/media-type/root</c> (issue #55; no flat
    /// <c>/media-type</c> collection, mirroring document types). Tree items expose only
    /// id/name/icon — alias and description require a single-item GET.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of media types mapped to <see cref="MediaTypeResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<MediaTypeResponse>>> GetMediaTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.MediaType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<MediaTypeResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new MediaTypeResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            Icon = i.Icon,
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>Gets a single media type by id (issue #55). Reads <c>GET /media-type/{id}</c>.</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The media type mapped to <see cref="MediaTypeResponse"/>.</returns>
    public Task<UmbracoResponse<MediaTypeResponse>> GetMediaTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var m = await _api
                    .Umbraco.Management.Api.V1.MediaType[id]
                    .GetAsync(cancellationToken: ct);
                return new MediaTypeResponse
                {
                    Id = m?.Id ?? id,
                    Name = m?.Name ?? "",
                    Alias = m?.Alias ?? "",
                    Description = m?.Description,
                    Icon = m?.Icon,
                    IsElement = m?.IsElement ?? false,
                    AllowedAsRoot = m?.AllowedAsRoot ?? false,
                };
            }
        );

    /// <summary>
    /// Creates a media type via <c>POST media-type</c> (generated client, issue #55). The id is
    /// client-generated (Umbraco 14+ accepts a supplied GUID), so the created type is echoed
    /// back with its id and the accepted fields without a follow-up read (the <c>201</c>
    /// response body is empty — guards the empty-payload class of #74). The API-required
    /// collections (allowed types, compositions, containers, properties) default to empty and
    /// the varies-by flags to false so a minimal name+alias create succeeds (issue #47 parity).
    /// </summary>
    /// <param name="request">The media type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media type (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<MediaTypeResponse>> CreateMediaTypeAsync(
        CreateMediaTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateMediaTypeRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Description = request.Description,
                    Icon = request.Icon,
                    IsElement = request.IsElement,
                    AllowedAsRoot = request.AllowedAsRoot,
                    VariesByCulture = false,
                    VariesBySegment = false,
                    AllowedMediaTypes = [],
                    Compositions = [],
                    Containers = [],
                    Properties = [],
                };
                await _api.Umbraco.Management.Api.V1.MediaType.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new MediaTypeResponse
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Description = request.Description,
                    Icon = request.Icon,
                    IsElement = request.IsElement,
                    AllowedAsRoot = request.AllowedAsRoot,
                };
            }
        );

    /// <summary>Deletes a media type via <c>DELETE media-type/{id}</c> (generated client).</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteMediaTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.MediaType[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Document Types ───────────────────────────────────────────────────────

    /// <summary>
    /// Lists document types from <c>tree/document-type/root</c> (issue #39 — no flat
    /// <c>/document-type</c> collection). Tree items expose <c>name</c> directly.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of document types mapped to <see cref="DocumentTypeResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<DocumentTypeResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DocumentTypeResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            IsElement = i.IsElement ?? false,
                        })
                        .ToList(),
                };
            }
        );

    public async Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => await GetAsync<DocumentTypeResponse>($"umbraco/management/api/v1/document-type/{id}", ct);

    public async Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(
        CreateDocumentTypeRequest request,
        CancellationToken ct = default
    ) =>
        await PostAsync<CreateDocumentTypeRequest, DocumentTypeResponse>(
            "umbraco/management/api/v1/document-type",
            request,
            ct
        );

    /// <summary>Deletes a document type via <c>DELETE document-type/{id}</c> (generated client).</summary>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteDocumentTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentType[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Data Types ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lists data types from <c>tree/data-type/root</c> (issue #39 — no flat
    /// <c>/data-type</c> collection). The editor alias is not carried on tree items, so
    /// only id/name/editorUiAlias are populated for the list view.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of data types mapped to <see cref="DataTypeResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.DataType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<DataTypeResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DataTypeResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            EditorUiAlias = i.EditorUiAlias,
                        })
                        .ToList(),
                };
            }
        );

    public async Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => await GetAsync<DataTypeResponse>($"umbraco/management/api/v1/data-type/{id}", ct);

    /// <summary>
    /// Creates a data type via <c>POST data-type</c> (generated client, issue #59). The id is
    /// client-generated and echoed back; editor configuration values default to empty.
    /// </summary>
    /// <param name="request">The data type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created data type (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<DataTypeResponse>> CreateDataTypeAsync(
        CreateDataTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateDataTypeRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    EditorAlias = request.EditorAlias,
                    EditorUiAlias = request.EditorUiAlias,
                    Values = [],
                };
                await _api.Umbraco.Management.Api.V1.DataType.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new DataTypeResponse
                {
                    Id = id,
                    Name = request.Name,
                    EditorAlias = request.EditorAlias,
                    EditorUiAlias = request.EditorUiAlias,
                };
            }
        );

    /// <summary>
    /// Updates a data type via <c>PUT data-type/{id}</c> (generated client, issue #59). The PUT
    /// is a full replace, so the current data type is read first: null fields on
    /// <paramref name="request"/> are preserved and — critically — the editor configuration
    /// <c>values</c> are carried over so an update cannot wipe them.
    /// </summary>
    /// <param name="id">The data type id.</param>
    /// <param name="request">The fields to change; null fields are preserved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        Guid id,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var current =
                    await _api
                        .Umbraco.Management.Api.V1.DataType[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No data type found with id '{id}'.");
                var body = new Gen.UpdateDataTypeRequestModel
                {
                    Name = request.Name ?? current.Name ?? "",
                    EditorAlias = request.EditorAlias ?? current.EditorAlias ?? "",
                    EditorUiAlias = request.EditorUiAlias ?? current.EditorUiAlias ?? "",
                    // Preserve the existing editor configuration values (not exposed by the CLI).
                    Values = current.Values ?? [],
                };
                await _api
                    .Umbraco.Management.Api.V1.DataType[id]
                    .PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Deletes a data type via <c>DELETE data-type/{id}</c> (generated client, issue #59).</summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteDataTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DataType[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Languages ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists configured languages (issue #41 — <c>GET /language</c> returns a paged
    /// <c>{total,items}</c> object, not a bare array). Requests a large page so callers
    /// keep the "all languages" semantics of the previous signature.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The configured languages mapped to <see cref="LanguageResponse"/>.</returns>
    public Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IEnumerable<LanguageResponse>>(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Language.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = 0;
                        c.QueryParameters.Take = 1000;
                    },
                    ct
                );
                return (paged?.Items ?? [])
                    .Select(l => new LanguageResponse
                    {
                        IsoCode = l.IsoCode ?? "",
                        Name = l.Name ?? "",
                        IsDefault = l.IsDefault ?? false,
                        IsMandatory = l.IsMandatory ?? false,
                        FallbackIsoCode = l.FallbackIsoCode,
                    })
                    .ToList();
            }
        );

    /// <summary>
    /// Creates a language via <c>POST language</c> (generated client). The endpoint is keyed
    /// by ISO code (no server-assigned id) and returns <c>201</c> with an empty body, so the
    /// accepted request is echoed back as the created language (fixes the empty-payload class
    /// of #74 for this resource).
    /// </summary>
    /// <param name="request">The language to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created language, or a mapped failure.</returns>
    public Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(
        CreateLanguageRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.CreateLanguageRequestModel
                {
                    IsoCode = request.IsoCode,
                    Name = request.Name,
                    IsDefault = request.IsDefault,
                    IsMandatory = request.IsMandatory,
                    FallbackIsoCode = request.FallbackIsoCode,
                };
                await _api.Umbraco.Management.Api.V1.Language.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new LanguageResponse
                {
                    IsoCode = request.IsoCode,
                    Name = request.Name,
                    IsDefault = request.IsDefault,
                    IsMandatory = request.IsMandatory,
                    FallbackIsoCode = request.FallbackIsoCode,
                };
            }
        );

    /// <summary>
    /// Updates a language via <c>PUT language/{isoCode}</c> (generated client, issue #59). The
    /// 200 response has no body, so the accepted request is echoed back as the updated language.
    /// </summary>
    /// <param name="isoCode">The ISO code of the language to update.</param>
    /// <param name="request">The replacement name/flags/fallback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated language, or a mapped failure.</returns>
    public Task<UmbracoResponse<LanguageResponse>> UpdateLanguageAsync(
        string isoCode,
        UpdateLanguageRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Read-merge: the PUT is a full replace, so preserve any field left null on the
                // request (avoids silently clearing default/mandatory/fallback on a name change).
                var current =
                    await _api
                        .Umbraco.Management.Api.V1.Language[isoCode]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No language found with ISO code '{isoCode}'.");
                var name = request.Name ?? current.Name ?? "";
                var isDefault = request.IsDefault ?? current.IsDefault ?? false;
                var isMandatory = request.IsMandatory ?? current.IsMandatory ?? false;
                var fallback = request.FallbackIsoCode ?? current.FallbackIsoCode;
                var body = new Gen.UpdateLanguageRequestModel
                {
                    Name = name,
                    IsDefault = isDefault,
                    IsMandatory = isMandatory,
                    FallbackIsoCode = fallback,
                };
                await _api
                    .Umbraco.Management.Api.V1.Language[isoCode]
                    .PutAsync(body, cancellationToken: ct);
                return new LanguageResponse
                {
                    IsoCode = isoCode,
                    Name = name,
                    IsDefault = isDefault,
                    IsMandatory = isMandatory,
                    FallbackIsoCode = fallback,
                };
            }
        );

    /// <summary>Deletes a language via <c>DELETE language/{isoCode}</c> (generated client).</summary>
    /// <param name="isoCode">The ISO code of the language to delete (the endpoint is keyed by iso code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteLanguageAsync(
        string isoCode,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Language[isoCode]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Templates ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists templates from <c>tree/template/root</c> (issue #39 — no flat
    /// <c>/template</c> collection). Tree items are named entities exposing id + name;
    /// alias/master are only available from a single-item GET.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of templates mapped to <see cref="TemplateResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.Template.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<TemplateResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new TemplateResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>
    /// Gets a single template by alias OR id (issue #44 — there is no <c>GET /template?alias=</c>
    /// endpoint; Umbraco only exposes <c>GET /template/{id}</c>). A GUID argument is used
    /// directly; otherwise the alias is resolved to an id via <c>item/template/search</c>
    /// (matched case-insensitively on the exact alias) before the by-id fetch.
    /// </summary>
    /// <param name="aliasOrId">The template alias (e.g. "master") or its id (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The template mapped to <see cref="TemplateResponse"/>.</returns>
    public Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(
        string aliasOrId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                Guid id;
                if (Guid.TryParse(aliasOrId, out var parsed))
                {
                    id = parsed;
                }
                else
                {
                    var search = await _api.Umbraco.Management.Api.V1.Item.Template.Search.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Query = aliasOrId;
                            c.QueryParameters.Take = 100;
                        },
                        ct
                    );
                    var match = (search?.Items ?? []).FirstOrDefault(t =>
                        string.Equals(t.Alias, aliasOrId, StringComparison.OrdinalIgnoreCase)
                    );
                    if (match?.Id is not { } matchedId)
                        throw NotFound($"No template found with alias '{aliasOrId}'.");
                    id = matchedId;
                }

                var t = await _api
                    .Umbraco.Management.Api.V1.Template[id]
                    .GetAsync(cancellationToken: ct);
                return new TemplateResponse
                {
                    Id = t?.Id ?? id,
                    Name = t?.Name ?? "",
                    Alias = t?.Alias ?? "",
                    MasterTemplate = t?.MasterTemplate?.Id is { } mid
                        ? new ContentTypeReference { Id = mid }
                        : null,
                };
            }
        );

    /// <summary>
    /// Creates a template via <c>POST template</c> (generated client, issue #59). The id is
    /// client-generated so the created template is echoed back with its id and accepted fields
    /// (the 201 body is empty).
    /// </summary>
    /// <param name="request">The template to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<TemplateResponse>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateTemplateRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Content = request.Content,
                };
                await _api.Umbraco.Management.Api.V1.Template.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new TemplateResponse
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                };
            }
        );

    /// <summary>
    /// Updates a template via <c>PUT template/{id}</c> (generated client, issue #59). The PUT is
    /// a full replace, so the current template is read first and any null field on
    /// <paramref name="request"/> is preserved — critically, omitting the content leaves the
    /// Razor body intact rather than blanking it.
    /// </summary>
    /// <param name="id">The template id.</param>
    /// <param name="request">The fields to change; null fields are preserved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> UpdateTemplateAsync(
        Guid id,
        UpdateTemplateRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var current =
                    await _api
                        .Umbraco.Management.Api.V1.Template[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No template found with id '{id}'.");
                var body = new Gen.UpdateTemplateRequestModel
                {
                    Name = request.Name ?? current.Name ?? "",
                    Alias = request.Alias ?? current.Alias ?? "",
                    Content = request.Content ?? current.Content ?? "",
                };
                await _api
                    .Umbraco.Management.Api.V1.Template[id]
                    .PutAsync(body, cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>Deletes a template via <c>DELETE template/{id}</c> (generated client, issue #59).</summary>
    /// <param name="id">The template id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteTemplateAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Template[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Members ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists members via <c>filter/member</c> (issue #39 — no flat <c>/member</c>
    /// collection). The <paramref name="group"/> argument is passed through as the free-text
    /// <c>filter</c> query. Full member items are returned (name via <c>variants[]</c>).
    /// </summary>
    /// <param name="group">Free-text filter (member name/email); null for all.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of members mapped to <see cref="MemberResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Filter.Member.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        if (!string.IsNullOrEmpty(group))
                            c.QueryParameters.Filter = group;
                    },
                    ct
                );
                return new PagedResponse<MemberResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapMember).ToList(),
                };
            }
        );

    /// <summary>
    /// Gets a single member by id (issue #42 — name/createDate come from <c>variants[]</c>).
    /// Reads <c>GET /member/{id}</c>.
    /// </summary>
    /// <param name="id">The member id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member mapped to <see cref="MemberResponse"/>.</returns>
    public Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var m = await _api
                    .Umbraco.Management.Api.V1.Member[id]
                    .GetAsync(cancellationToken: ct);
                return m is null ? new MemberResponse { Id = id } : MapMember(m);
            }
        );

    /// <summary>
    /// Resolves a member-type reference - an alias or a GUID id - to its id, mirroring
    /// <see cref="ResolveDocumentTypeIdAsync"/>: a GUID is used directly, otherwise the alias is
    /// resolved via the member-type item search and each candidate's full member type is fetched
    /// to compare its alias (the search item model carries no alias). See ADR 0004.
    /// </summary>
    /// <param name="aliasOrId">The member-type alias or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved member-type id.</returns>
    /// <exception cref="ApiException">No member type matches the alias (mapped to a 404).</exception>
    private async Task<Guid> ResolveMemberTypeIdAsync(string aliasOrId, CancellationToken ct)
    {
        if (Guid.TryParse(aliasOrId, out var parsed))
            return parsed;

        var search = await _api.Umbraco.Management.Api.V1.Item.MemberType.Search.GetAsync(
            c =>
            {
                c.QueryParameters.Query = aliasOrId;
                c.QueryParameters.Take = 100;
            },
            ct
        );
        foreach (var item in search?.Items ?? [])
        {
            if (item.Id is not { } candidateId)
                continue;
            var mt = await _api
                .Umbraco.Management.Api.V1.MemberType[candidateId]
                .GetAsync(cancellationToken: ct);
            if (string.Equals(mt?.Alias, aliasOrId, StringComparison.OrdinalIgnoreCase))
                return candidateId;
        }

        throw NotFound($"No member type found with alias '{aliasOrId}'.");
    }

    /// <summary>
    /// Creates a member via <c>POST member</c> (generated client, #79). Like content create, the
    /// member type is passed by alias and resolved to an id first; the display name goes in a
    /// variant and property values map to <see cref="UntypedNode"/>. The id is client-supplied
    /// (defaulting to a fresh GUID) so it is known despite the empty create response, which is
    /// echoed back with the accepted fields (consistent with the other migrated creates).
    /// </summary>
    /// <remarks>
    /// Umbraco requires a username, but the CLI collects only an email, so the email doubles as
    /// the username. If a distinct username is ever needed it becomes a new option; the live
    /// round-trip confirms this default is accepted.
    /// </remarks>
    /// <param name="request">The member to create (member type by alias, email, name, values).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created member (id + echoed fields), or a mapped failure.</returns>
    public Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var reference =
                    request.MemberType.Id != Guid.Empty
                        ? request.MemberType.Id.ToString()
                        : request.MemberType.Alias;
                var memberTypeId = await ResolveMemberTypeIdAsync(reference, ct);

                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateMemberRequestModel
                {
                    Id = id,
                    Email = request.Email,
                    // The CLI collects only an email; Umbraco requires a username, so reuse it.
                    Username = request.Email,
                    Password = request.Password,
                    IsApproved = request.IsApproved,
                    MemberType = new Gen.ReferenceByIdModel { Id = memberTypeId },
                    Variants = [new Gen.MemberVariantRequestModel { Name = request.Name }],
                    Values = request
                        .Values.Select(cv => new Gen.MemberValueModel
                        {
                            Alias = cv.Alias,
                            Culture = cv.Culture,
                            Segment = cv.Segment,
                            Value = UntypedNodeFactory.FromValue(cv.Value),
                        })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.Member.PostAsync(body, cancellationToken: ct);

                return new MemberResponse
                {
                    Id = id,
                    Email = request.Email,
                    Name = request.Name,
                    MemberType = new ContentTypeReference { Id = memberTypeId },
                    IsApproved = request.IsApproved,
                };
            }
        );

    /// <summary>
    /// Updates a member via <c>PUT member/{id}</c> (generated client, issue #59). The member
    /// PUT is a full replace, so this reads the current member first and merges only the
    /// requested changes over it — groups, property values, lockout/2FA state and password are
    /// preserved. The 200 response has no body, so the merged member is returned.
    /// </summary>
    /// <param name="id">The member id.</param>
    /// <param name="request">The partial changes to apply (email/name/approved).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated member, or a mapped failure.</returns>
    public Task<UmbracoResponse<MemberResponse>> UpdateMemberAsync(
        Guid id,
        UpdateMemberRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var m =
                    await _api.Umbraco.Management.Api.V1.Member[id].GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No member found with id '{id}'.");

                // Preserve every variant, overriding the name only when a new one was supplied.
                var variants = (m.Variants ?? [])
                    .Select(v => new Gen.MemberVariantRequestModel
                    {
                        Culture = v.Culture,
                        Segment = v.Segment,
                        Name = request.Name ?? v.Name ?? "",
                    })
                    .ToList();
                // A member with no variant yet (edge case) still needs one to carry a new name.
                if (variants.Count == 0 && request.Name is not null)
                    variants.Add(new Gen.MemberVariantRequestModel { Name = request.Name });

                var body = new Gen.UpdateMemberRequestModel
                {
                    Email = request.Email ?? m.Email ?? "",
                    Username = m.Username ?? "",
                    IsApproved = request.IsApproved ?? m.IsApproved ?? false,
                    IsLockedOut = m.IsLockedOut ?? false,
                    IsTwoFactorEnabled = m.IsTwoFactorEnabled ?? false,
                    Groups = (m.Groups ?? []).ToList(),
                    Values = (m.Values ?? [])
                        .Select(v => new Gen.MemberValueModel
                        {
                            Alias = v.Alias,
                            Culture = v.Culture,
                            Segment = v.Segment,
                            Value = v.Value,
                        })
                        .ToList(),
                    Variants = variants,
                };
                await _api
                    .Umbraco.Management.Api.V1.Member[id]
                    .PutAsync(body, cancellationToken: ct);

                // Echo the merged member (the PUT returns no body).
                var mapped = MapMember(m);
                return mapped with
                {
                    Email = request.Email ?? mapped.Email,
                    Name = request.Name ?? mapped.Name,
                    IsApproved = request.IsApproved ?? mapped.IsApproved,
                };
            }
        );

    /// <summary>Deletes a member via <c>DELETE member/{id}</c> (generated client).</summary>
    /// <param name="id">The member id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteMemberAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.Member[id].DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Member Types ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lists member types from <c>tree/member-type/root</c> (issue #56; no flat
    /// <c>/member-type</c> collection). Tree items expose only id/name/icon — alias and
    /// description require a single-item GET.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of member types mapped to <see cref="MemberTypeResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<MemberTypeResponse>>> GetMemberTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.MemberType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<MemberTypeResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new MemberTypeResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            Icon = i.Icon,
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>Gets a single member type by id (issue #56). Reads <c>GET /member-type/{id}</c>.</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member type mapped to <see cref="MemberTypeResponse"/>.</returns>
    public Task<UmbracoResponse<MemberTypeResponse>> GetMemberTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var m = await _api
                    .Umbraco.Management.Api.V1.MemberType[id]
                    .GetAsync(cancellationToken: ct);
                return new MemberTypeResponse
                {
                    Id = m?.Id ?? id,
                    Name = m?.Name ?? "",
                    Alias = m?.Alias ?? "",
                    Description = m?.Description,
                    Icon = m?.Icon,
                };
            }
        );

    /// <summary>
    /// Creates a member type via <c>POST member-type</c> (generated client, issue #56). The id
    /// is client-generated (Umbraco 14+ accepts a supplied GUID), so the created type is echoed
    /// back with its id and the accepted fields without a follow-up read (the <c>201</c>
    /// response body is empty). The API-required collections default to empty and the varies-by
    /// flags to false so a minimal name+alias create succeeds (issue #47 parity).
    /// </summary>
    /// <param name="request">The member type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created member type (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<MemberTypeResponse>> CreateMemberTypeAsync(
        CreateMemberTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateMemberTypeRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Description = request.Description,
                    Icon = request.Icon,
                    IsElement = false,
                    AllowedAsRoot = false,
                    VariesByCulture = false,
                    VariesBySegment = false,
                    Compositions = [],
                    Containers = [],
                    Properties = [],
                };
                await _api.Umbraco.Management.Api.V1.MemberType.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new MemberTypeResponse
                {
                    Id = id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Description = request.Description,
                    Icon = request.Icon,
                };
            }
        );

    /// <summary>Deletes a member type via <c>DELETE member-type/{id}</c> (generated client).</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteMemberTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.MemberType[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        await GetAsync<PagedResponse<UserResponse>>(
            $"umbraco/management/api/v1/user?skip={skip}&take={take}",
            ct
        );

    public async Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => await GetAsync<UserResponse>($"umbraco/management/api/v1/user/{id}", ct);

    /// <summary>
    /// Invites a user via <c>POST user/invite</c> (generated client). The endpoint sends the
    /// invitation email and returns no body, so an empty success response is returned.
    /// </summary>
    /// <param name="request">The invite details (email, name, user groups).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.InviteUserRequestModel
                {
                    Email = request.Email,
                    Name = request.Name,
                    UserName = request.UserName,
                    Message = request.Message,
                    UserGroupIds = request
                        .UserGroupIds.Select(g => new Gen.ReferenceByIdModel { Id = g.Id })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.User.Invite.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    // ── Dictionary ───────────────────────────────────────────────────────────

    public async Task<
        UmbracoResponse<PagedResponse<DictionaryItemResponse>>
    > GetDictionaryItemsAsync(int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<DictionaryItemResponse>>(
            $"umbraco/management/api/v1/dictionary?skip={skip}&take={take}",
            ct
        );

    /// <summary>
    /// Gets a dictionary item by its human key (name) OR id (issue #44 — the endpoint is
    /// <c>GET /dictionary/{id}</c> keyed by GUID, so a human key 404'd despite the help
    /// saying "by key"). A GUID argument is fetched directly; otherwise the key is
    /// resolved to an id by matching the item name in the dictionary list.
    /// </summary>
    /// <param name="keyOrId">The dictionary item key (name) or its id (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dictionary item, or a 404 failure when no matching key exists.</returns>
    public async Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(
        string keyOrId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(keyOrId, out var id))
        {
            // Resolve the human key to an id via the list (items expose id + name).
            var list = await GetDictionaryItemsAsync(0, 1000, ct);
            if (!list.IsSuccess)
                return UmbracoResponse<DictionaryItemResponse>.Failure(
                    list.StatusCode,
                    list.ErrorMessage ?? "Could not list dictionary items."
                );

            var match = (list.Data?.Items ?? []).FirstOrDefault(d =>
                string.Equals(d.Name, keyOrId, StringComparison.OrdinalIgnoreCase)
            );
            if (match is null)
                return UmbracoResponse<DictionaryItemResponse>.Failure(
                    404,
                    $"No dictionary item found with key '{keyOrId}'."
                );
            id = match.Id;
        }

        return await GetAsync<DictionaryItemResponse>(
            $"umbraco/management/api/v1/dictionary/{id}",
            ct
        );
    }

    /// <summary>
    /// Creates a dictionary item via <c>POST dictionary</c> (generated client). The id is
    /// client-generated (Umbraco 14+ accepts a supplied GUID) so the created item can be
    /// echoed back fully populated without a follow-up read; the <c>201</c> response has an
    /// empty body.
    /// </summary>
    /// <param name="request">The dictionary item to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created dictionary item (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(
        CreateDictionaryItemRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateDictionaryItemRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    Translations = request
                        .Translations.Select(t => new Gen.DictionaryItemTranslationModel
                        {
                            IsoCode = t.IsoCode,
                            Translation = t.Translation,
                        })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.Dictionary.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new DictionaryItemResponse
                {
                    Id = id,
                    Name = request.Name,
                    Translations = request.Translations.ToList(),
                };
            }
        );

    /// <summary>Deletes a dictionary item via <c>DELETE dictionary/{id}</c> (generated client, issue #59).</summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteDictionaryItemAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Webhooks ──────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        await GetAsync<PagedResponse<WebhookResponse>>(
            $"umbraco/management/api/v1/webhook?skip={skip}&take={take}",
            ct
        );

    /// <summary>
    /// Creates a webhook via <c>POST webhook</c> (generated client). The id is
    /// client-generated (Umbraco 14+ accepts a supplied GUID), so the created webhook is
    /// echoed back with its id and the accepted request fields without a follow-up read
    /// (the <c>201</c> response body is empty). The request's event names (strings) are
    /// echoed as <see cref="WebhookEvent"/> objects to match the read shape (#46).
    /// </summary>
    /// <param name="request">The webhook to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created webhook (with the generated id), or a mapped failure.</returns>
    public Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateWebhookRequestModel
                {
                    Id = id,
                    Name = request.Name,
                    Description = request.Description,
                    Url = request.Url,
                    Events = request.Events.ToList(),
                    Enabled = request.Enabled,
                    ContentTypeKeys = request.ContentTypeKeys.Select(k => (Guid?)k).ToList(),
                    Headers = MapWebhookHeaders(request.Headers),
                };
                await _api.Umbraco.Management.Api.V1.Webhook.PostAsync(body, cancellationToken: ct);
                return new WebhookResponse
                {
                    Id = id,
                    Name = request.Name,
                    Description = request.Description,
                    Url = request.Url,
                    Enabled = request.Enabled,
                    Events = request
                        .Events.Select(e => new WebhookEvent { EventName = e })
                        .ToList(),
                    ContentTypeKeys = request.ContentTypeKeys.ToList(),
                    // Echo the headers dictionary as-is (empty when none) to match the shape
                    // the webhook read path produces from the API's `headers` object.
                    Headers = request.Headers,
                };
            }
        );

    /// <summary>
    /// Maps command-facing webhook headers onto the generated open "headers" object, whose
    /// key/value pairs live in its <c>AdditionalData</c> bag. Returns null when there are no
    /// headers so the serialized body omits the member entirely.
    /// </summary>
    /// <param name="headers">The header key/value pairs from the create request.</param>
    /// <returns>The generated headers object, or null when empty.</returns>
    private static Gen.CreateWebhookRequestModel_headers? MapWebhookHeaders(
        Dictionary<string, string> headers
    )
    {
        if (headers is null || headers.Count == 0)
            return null;
        var mapped = new Gen.CreateWebhookRequestModel_headers();
        foreach (var (key, value) in headers)
            mapped.AdditionalData[key] = value;
        return mapped;
    }

    /// <summary>Deletes a webhook via <c>DELETE webhook/{id}</c> (generated client).</summary>
    /// <param name="id">The webhook id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteWebhookAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.Webhook[id].DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<UmbracoResponse<TResponse>> GetAsync<TResponse>(
        string url,
        CancellationToken ct
    ) =>
        GuardedAsync(
            async () =>
            {
                var response = await _http.GetAsync(url, ct);
                return await DeserializeAsync<TResponse>(response, ct);
            },
            ct
        );

    private Task<UmbracoResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        CancellationToken ct
    ) =>
        GuardedAsync(
            async () =>
            {
                var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
                return await DeserializeAsync<TResponse>(response, ct);
            },
            ct
        );

    private Task<UmbracoResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        CancellationToken ct
    ) =>
        GuardedAsync(
            async () =>
            {
                var response = await _http.PutAsJsonAsync(url, body, JsonOptions, ct);
                return await DeserializeAsync<TResponse>(response, ct);
            },
            ct
        );

    private Task<UmbracoResponse<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string url,
        HttpContent content,
        CancellationToken ct
    ) =>
        GuardedAsync(
            async () =>
            {
                var request = new HttpRequestMessage(method, url) { Content = content };
                var response = await _http.SendAsync(request, ct);
                return await DeserializeAsync<TResponse>(response, ct);
            },
            ct
        );

    /// <summary>
    /// Converts transport-level failures (host unreachable, timeout, unreadable body)
    /// into a failed <see cref="UmbracoResponse{T}"/> with status code 0, so callers
    /// see a normal failure instead of an exception. A genuine cancellation requested
    /// via <paramref name="ct"/> is left to propagate.
    /// </summary>
    private static async Task<UmbracoResponse<T>> GuardedAsync<T>(
        Func<Task<UmbracoResponse<T>>> action,
        CancellationToken ct
    )
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UmbracoResponse<T>.Failure(0, "The request to the Umbraco instance timed out.");
        }
        catch (HttpRequestException ex)
        {
            return UmbracoResponse<T>.Failure(
                0,
                $"Could not reach the Umbraco instance: {ex.Message}"
            );
        }
        catch (JsonException ex)
        {
            return UmbracoResponse<T>.Failure(
                0,
                $"The Umbraco instance returned an unreadable response: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Runs a generated-client call and wraps the outcome in a <see cref="UmbracoResponse{T}"/>.
    /// Kiota signals HTTP errors by throwing <see cref="ApiException"/> (and transport
    /// errors as <see cref="HttpRequestException"/>); this converts them all into a failed
    /// response so the command layer keeps its "errors are data" contract (never throws for
    /// HTTP-level failures). A genuine caller cancellation is left to propagate.
    /// </summary>
    /// <typeparam name="T">The mapped payload type.</typeparam>
    /// <param name="ct">The caller's cancellation token; a genuine cancellation is rethrown.</param>
    /// <param name="action">The generated-client call, already mapped to <typeparamref name="T"/>.</param>
    /// <returns>A success response with the payload, or a failure with the status + message.</returns>
    private static async Task<UmbracoResponse<T>> GuardedApiAsync<T>(
        CancellationToken ct,
        Func<Task<T>> action
    )
    {
        try
        {
            return UmbracoResponse<T>.Success(await action());
        }
        catch (Gen.ProblemDetails pd)
        {
            // Kiota throws the generated ProblemDetails (which derives from ApiException)
            // for RFC-9110 error bodies, but its Message is the useless base default
            // ("Exception of type '...ProblemDetails' was thrown."). Build a readable
            // message from the real fields so 404s and other errors are legible (#48).
            var status = pd.ResponseStatusCode != 0 ? pd.ResponseStatusCode : pd.Status ?? 0;
            var baseMessage =
                !string.IsNullOrWhiteSpace(pd.Detail) ? pd.Detail!
                : !string.IsNullOrWhiteSpace(pd.Title) ? pd.Title!
                : $"Error {status}";
            // Append the field-level "errors" map (e.g. "isoCode: Required") so a rejected
            // write tells the user WHICH field failed, matching the hand-written path's
            // BuildErrorAsync/FormatValidationErrors behaviour (#48). On the Kiota path the
            // map has no typed property; it lands in AdditionalData as an UntypedNode.
            var fieldErrors = FormatProblemDetailsErrors(pd);
            var message = fieldErrors is null ? baseMessage : $"{baseMessage} ({fieldErrors})";
            return UmbracoResponse<T>.Failure(status, message);
        }
        catch (ApiException ex)
        {
            // ResponseStatusCode is 0 when Kiota never got an HTTP response.
            return UmbracoResponse<T>.Failure(
                ex.ResponseStatusCode,
                string.IsNullOrWhiteSpace(ex.Message)
                    ? $"Error {ex.ResponseStatusCode}"
                    : ex.Message
            );
        }
        catch (HttpRequestException ex)
        {
            return UmbracoResponse<T>.Failure(
                0,
                $"Could not reach the Umbraco instance: {ex.Message}"
            );
        }
        // A timeout surfaces as a cancellation whose token is NOT the caller's; a genuine
        // caller cancellation (ct signalled) is rethrown so callers can observe it.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UmbracoResponse<T>.Failure(0, "The request to the Umbraco instance timed out.");
        }
    }

    /// <summary>
    /// Builds a 404 <see cref="ApiException"/> for client-side resolution failures (e.g. an
    /// alias/key that matches no resource), so <see cref="GuardedApiAsync{T}"/> maps it to a
    /// normal 404 failure rather than a thrown exception.
    /// </summary>
    /// <param name="message">The not-found message to surface.</param>
    /// <returns>An <see cref="ApiException"/> with status 404.</returns>
    private static ApiException NotFound(string message) =>
        new(message) { ResponseStatusCode = 404 };

    /// <summary>Returns the invariant (or first available) variant name from a document tree item.</summary>
    /// <param name="item">The generated document tree item.</param>
    /// <returns>The display name, or an empty string when no variant is present.</returns>
    private static string DocumentName(Gen.DocumentTreeItemResponseModel item) =>
        (item.Variants ?? []).FirstOrDefault()?.Name ?? "";

    /// <summary>Maps a generated document tree item onto the command-facing <see cref="ContentItemResponse"/>.</summary>
    /// <param name="item">The generated document tree item.</param>
    /// <returns>The mapped content item (name flattened from variants, published derived from variant state).</returns>
    private static ContentItemResponse MapDocumentTreeItem(
        Gen.DocumentTreeItemResponseModel item
    ) =>
        new()
        {
            Id = item.Id ?? Guid.Empty,
            Name = DocumentName(item),
            ContentType = item.DocumentType?.Id is { } dtId
                ? new ContentTypeReference { Id = dtId }
                : null,
            Parent = item.Parent?.Id is { } pId ? new ContentParentReference { Id = pId } : null,
            IsPublished = (item.Variants ?? []).Any(v =>
                v.State
                    is Gen.DocumentVariantStateModel.Published
                        or Gen.DocumentVariantStateModel.PublishedPendingChanges
            ),
            CreateDate = item.CreateDate ?? default,
        };

    /// <summary>Maps a generated media tree item onto the command-facing <see cref="MediaItemResponse"/>.</summary>
    /// <param name="item">The generated media tree item.</param>
    /// <returns>The mapped media item (name flattened from variants).</returns>
    private static MediaItemResponse MapMediaTreeItem(Gen.MediaTreeItemResponseModel item) =>
        new()
        {
            Id = item.Id ?? Guid.Empty,
            Name = (item.Variants ?? []).FirstOrDefault()?.Name ?? "",
            MediaType = item.MediaType?.Id is { } mtId
                ? new ContentTypeReference { Id = mtId }
                : null,
            Parent = item.Parent?.Id is { } pId ? new ContentParentReference { Id = pId } : null,
            CreateDate = item.CreateDate ?? default,
        };

    /// <summary>Maps a generated member item onto the command-facing <see cref="MemberResponse"/>.</summary>
    /// <param name="item">The generated member item.</param>
    /// <returns>The mapped member (name flattened from variants).</returns>
    private static MemberResponse MapMember(Gen.MemberResponseModel item)
    {
        var variant = (item.Variants ?? []).FirstOrDefault();
        return new MemberResponse
        {
            Id = item.Id ?? Guid.Empty,
            Email = item.Email ?? "",
            Name = variant?.Name ?? "",
            MemberType = item.MemberType?.Id is { } mtId
                ? new ContentTypeReference { Id = mtId }
                : null,
            IsApproved = item.IsApproved ?? false,
            IsLockedOut = item.IsLockedOut ?? false,
            CreateDate = variant?.CreateDate ?? default,
        };
    }

    private static async Task<UmbracoResponse<TResponse>> DeserializeAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (!response.IsSuccessStatusCode)
            return await BuildErrorAsync<TResponse>(response, ct);

        var status = (int)response.StatusCode;

        // Umbraco returns 201 Created (and 200/202) with an EMPTY body for writes.
        // Treat any 2xx with no content as success — previously only 204 was handled,
        // so a successful create surfaced a bogus "unreadable response" error (#43).
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            // Surface the new id when the API returns it via the Location header
            // (e.g. Location: .../webhook/{id}). Every response DTO exposes an "id"
            // JSON property, so hydrating {"id":"..."} yields a DTO carrying just the
            // new id — enough for callers/agents to chain follow-up calls (#43).
            var newId = TryGetIdFromLocation(response);
            if (newId is not null)
            {
                var withId = JsonSerializer.Deserialize<TResponse>(
                    $$"""{"id":"{{newId}}"}""",
                    JsonOptions
                );
                if (withId is not null)
                    return UmbracoResponse<TResponse>.Success(withId, status);
            }
            return UmbracoResponse<TResponse>.Success(default!, status);
        }

        var data = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
        return data is not null
            ? UmbracoResponse<TResponse>.Success(data, status)
            : UmbracoResponse<TResponse>.Failure(status, "Empty response body");
    }

    /// <summary>
    /// Extracts the trailing GUID from the <c>Location</c> response header of a create
    /// (e.g. <c>/umbraco/management/api/v1/webhook/{id}</c>). Returns null when there is
    /// no Location header or its last segment is not a GUID.
    /// </summary>
    /// <param name="response">The HTTP response from a create call.</param>
    /// <returns>The new resource id as a string, or null.</returns>
    private static string? TryGetIdFromLocation(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString();
        if (string.IsNullOrEmpty(location))
            return null;
        var lastSegment = location.TrimEnd('/').Split('/').LastOrDefault();
        return Guid.TryParse(lastSegment, out var id) ? id.ToString() : null;
    }

    private static async Task<UmbracoResponse<T>> BuildErrorAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        // Empty body (typically a bare 404): fall back to the HTTP reason phrase so the
        // user sees "Not Found" rather than a blank message (#48).
        if (string.IsNullOrWhiteSpace(body))
        {
            var reason = response.ReasonPhrase;
            return UmbracoResponse<T>.Failure(
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(reason) ? $"Error {(int)response.StatusCode}" : reason
            );
        }

        string message = body;
        try
        {
            var err = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
            // Umbraco returns RFC-9110 ProblemDetails: {title, detail, errors}. Prefer the
            // human title/detail, then append the field-level "errors" so a failed create
            // tells the user WHICH field failed instead of a generic message (#48).
            var baseMessage =
                err.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()
                : err.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String
                    ? title.GetString()
                : null;

            var fieldErrors = FormatValidationErrors(err);

            message = (baseMessage, fieldErrors) switch
            {
                (not null, not null) => $"{baseMessage} ({fieldErrors})",
                (not null, null) => baseMessage,
                (null, not null) => fieldErrors,
                _ => body,
            };
        }
        catch (JsonException)
        {
            // Non-JSON error body: surface it verbatim.
            message = body;
        }
        return UmbracoResponse<T>.Failure((int)response.StatusCode, message);
    }

    /// <summary>
    /// Flattens a ProblemDetails <c>errors</c> member into a single readable string.
    /// Handles both the object form (<c>{"field":["msg"]}</c>) and the array-of-objects
    /// form (<c>[{"field":["msg"]}]</c>) that Umbraco can return (#48).
    /// </summary>
    /// <param name="problem">The parsed ProblemDetails root element.</param>
    /// <returns>A "field: message; ..." string, or null when there are no field errors.</returns>
    private static string? FormatValidationErrors(JsonElement problem)
    {
        if (!problem.TryGetProperty("errors", out var errors))
            return null;

        var parts = new List<string>();

        // Collects "field: msg1, msg2" from an object whose values are string arrays.
        void CollectFromObject(JsonElement obj)
        {
            foreach (var field in obj.EnumerateObject())
            {
                var messages =
                    field.Value.ValueKind == JsonValueKind.Array
                        ? string.Join(", ", field.Value.EnumerateArray().Select(v => v.GetString()))
                        : field.Value.ToString();
                parts.Add($"{field.Name}: {messages}");
            }
        }

        if (errors.ValueKind == JsonValueKind.Object)
            CollectFromObject(errors);
        else if (errors.ValueKind == JsonValueKind.Array)
            foreach (var element in errors.EnumerateArray())
                if (element.ValueKind == JsonValueKind.Object)
                    CollectFromObject(element);

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    /// <summary>
    /// Flattens the RFC-9110 field-level <c>errors</c> map from a generated
    /// <see cref="Gen.ProblemDetails"/> into a readable "field: msg; ..." string, so a
    /// write rejected on the Kiota path keeps the "which field failed" detail the
    /// hand-written path surfaces via <see cref="FormatValidationErrors(JsonElement)"/>
    /// (#48). The generated ProblemDetails has no typed <c>errors</c> property, so the map
    /// arrives under <see cref="Gen.ProblemDetails.AdditionalData"/> as a Kiota
    /// <see cref="UntypedNode"/> tree (an object of field -> array-of-message-strings).
    /// </summary>
    /// <param name="pd">The generated ProblemDetails thrown by a failed Kiota write.</param>
    /// <returns>A "field: message; ..." string, or null when there are no field errors.</returns>
    internal static string? FormatProblemDetailsErrors(Gen.ProblemDetails pd)
    {
        if (pd.AdditionalData is null || !pd.AdditionalData.TryGetValue("errors", out var raw))
            return null;

        // Collapses a single UntypedNode (string/bool/number) to its text form; anything
        // else (nested object) is ignored, matching the string-array shape Umbraco returns.
        static string NodeToString(UntypedNode node) =>
            node switch
            {
                UntypedString s => s.GetValue() ?? "",
                UntypedBoolean b => b.GetValue().ToString(),
                UntypedInteger i => i.GetValue().ToString(),
                UntypedLong l => l.GetValue().ToString(),
                UntypedDouble d => d.GetValue().ToString(CultureInfo.InvariantCulture),
                UntypedDecimal m => m.GetValue().ToString(CultureInfo.InvariantCulture),
                _ => "",
            };

        var parts = new List<string>();

        // Flattens one {"field": ["msg", ...]} object into "field: msg1, msg2" entries.
        void CollectFromObject(UntypedObject obj)
        {
            foreach (var field in obj.GetValue())
            {
                // Each field value is normally an array of message strings, but tolerate a
                // bare scalar too.
                var messages = field.Value is UntypedArray arr
                    ? string.Join(
                        ", ",
                        arr.GetValue().Select(NodeToString).Where(s => !string.IsNullOrEmpty(s))
                    )
                    : NodeToString(field.Value);
                if (!string.IsNullOrEmpty(messages))
                    parts.Add($"{field.Key}: {messages}");
            }
        }

        // Umbraco returns the errors map either as an object ({"field":["msg"]}) or, less
        // commonly, as an array of such objects ([{"field":["msg"]}]) — handle both, matching
        // the hand-written FormatValidationErrors on the HttpClient path (#48).
        if (raw is UntypedObject errorsObj)
            CollectFromObject(errorsObj);
        else if (raw is UntypedArray errorsArr)
            foreach (var element in errorsArr.GetValue())
                if (element is UntypedObject elementObj)
                    CollectFromObject(elementObj);

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }
}
