using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Migration is complete (#50, #79). Every call goes through the generated request builders
/// via <see cref="GuardedApiAsync{T}"/> - reads, deletes, creates, the content write path
/// (with document-type alias→id resolution and a JSON→UntypedNode value converter), and the
/// two-step temporary-file media upload. The hand-written <see cref="HttpClient"/> path and
/// its helper stack have been removed; the client is now fully generated.
/// </summary>
public sealed partial class UmbracoManagementClient : IUmbracoManagementClient
{
    /// <summary>The Kiota-generated Management API client.</summary>
    private readonly UmbracoApiClient _api;

    /// <summary>
    /// The Kiota request adapter backing <see cref="_api"/>. Retained because a
    /// <see cref="MultipartBody"/> (used for the temporary-file upload) needs an adapter to
    /// resolve its per-part serializers, and the builder's own adapter is not publicly exposed.
    /// </summary>
    private readonly IRequestAdapter _adapter;

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
                        ? new ContentTypeRef
                        {
                            Id = dtId,
                            Alias = await DocumentTypeAliasAsync(dtId, ct),
                        }
                        : null,
                    IsPublished = (d?.Variants ?? []).Any(v =>
                        v.State
                            is Gen.DocumentVariantStateModel.Published
                                or Gen.DocumentVariantStateModel.PublishedPendingChanges
                    ),
                    CreateDate = variant?.CreateDate ?? default,
                    UpdateDate = variant?.UpdateDate ?? default,
                    // #168: the whole document, not a summary of its first variant. Without these
                    // a get -> edit -> update round-trip is impossible through the CLI alone.
                    Values = MapValueResponses(d?.Values),
                    Variants = MapVariantResponses(d?.Variants),
                    Template = d?.Template?.Id is { } tplId
                        ? new ContentTemplateReference { Id = tplId }
                        : null,
                };
            }
        );

    /// <summary>
    /// Document-type tree leaf ids (folders excluded), captured on the first alias resolution of
    /// this client's lifetime so a second resolution does not re-walk the tree.
    /// </summary>
    private List<Guid>? _documentTypeLeafIds;

    /// <summary>Document-type alias to id, filled in as candidates are read by-id.</summary>
    private readonly Dictionary<string, Guid> _documentTypeAliases = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Document-type ids already read by-id, so a candidate is never fetched twice.</summary>
    private readonly HashSet<Guid> _documentTypeAliasesRead = [];

    /// <summary>
    /// Resolves a document-type reference - an alias (e.g. <c>textPage</c>) or a GUID id - to
    /// its id, which is what the generated create model requires. A value that parses as a GUID
    /// is used directly.
    /// </summary>
    /// <remarks>
    /// An alias is resolved by walking the document-type <em>tree</em> and comparing the alias on
    /// each type read by-id - deliberately NOT via the document-type item search. The item search
    /// indexes only the <em>name</em>, so searching for an alias finds nothing whenever the alias
    /// differs from the name by more than case - which is the Umbraco norm for any multi-word type
    /// ("Text Page" -> <c>textPage</c>). An earlier revision of #79 used the search and so failed
    /// to resolve exactly those types; see ADR 0004.
    ///
    /// Neither the tree item model nor the item-search model exposes Alias, so each candidate
    /// costs one by-id GET. Results are cached per client instance (the tree walk and every alias
    /// seen), and the scan short-circuits on the first match, so the common case of one alias per
    /// invocation stops as soon as it is found rather than reading every type.
    /// </remarks>
    /// <param name="aliasOrId">The document-type alias or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved document-type id.</returns>
    /// <exception cref="ApiException">No document type matches the alias (mapped to a 404).</exception>
    private async Task<Guid> FindDocumentTypeIdAsync(string aliasOrId, CancellationToken ct)
    {
        // Already resolved (or seen while resolving something else) on this client instance.
        if (_documentTypeAliases.TryGetValue(aliasOrId, out var cached))
            return cached;

        _documentTypeLeafIds ??= await CollectTreeLeafIdsAsync(FetchDocumentTypeTreePageAsync, ct);

        foreach (var candidateId in _documentTypeLeafIds)
        {
            // Skip candidates already read: their alias is in the cache, which missed above.
            if (!_documentTypeAliasesRead.Add(candidateId))
                continue;

            var dt = await _api
                .Umbraco.Management.Api.V1.DocumentType[candidateId]
                .GetAsync(cancellationToken: ct);
            if (dt?.Alias is { } alias)
                _documentTypeAliases[alias] = candidateId;
            if (string.Equals(dt?.Alias, aliasOrId, StringComparison.OrdinalIgnoreCase))
                return candidateId;
        }

        throw NotFound(
            $"No document type found with alias '{aliasOrId}'. Use 'umbraco content-types list' "
                + "to find one, or pass a document type id."
        );
    }

    /// <summary>
    /// Fetches one page of the document-type tree: the root level when
    /// <paramref name="parentId"/> is null, otherwise the children of that folder.
    /// </summary>
    /// <param name="parentId">The parent folder id, or null for the tree root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's items as (id, is-folder) pairs.</returns>
    private async Task<IReadOnlyList<(Guid Id, bool IsFolder)>> FetchDocumentTypeTreePageAsync(
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        var items = parentId is null
            ? (
                await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                )
            )?.Items?.Select(i => (i.Id, i.IsFolder))
            : (
                await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parentId;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                )
            )?.Items?.Select(i => (i.Id, i.IsFolder));

        return
        [
            .. (items ?? [])
                .Where(i => i.Id is not null)
                .Select(i => (i.Id!.Value, i.IsFolder ?? false)),
        ];
    }

    /// <summary>
    /// Walks a Management-API tree breadth-first: the one walker behind every "all the types" read
    /// and resolver. For each node, <c>Descend</c> says whether to page through its children (a
    /// folder; a master template) and <c>Include</c> whether it is part of the result (a type, not
    /// a folder). Paged 100 at a time, each parent visited once, and stopped at 10,000 results as
    /// a backstop against a pathological or cyclic tree.
    /// </summary>
    /// <typeparam name="T">The mapped item type.</typeparam>
    /// <param name="fetchPage">Fetches a page for a parent (null = the root): (parent, skip, take, ct).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every included item, in breadth-first order.</returns>
    private static async Task<List<T>> CollectTreeAsync<T>(
        Func<
            Guid?,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<(Guid Id, bool Descend, bool Include, T Item)>>
        > fetchPage,
        CancellationToken ct
    )
    {
        const int pageSize = 100;
        const int maxResults = 10_000;

        var results = new List<T>();
        var pending = new Queue<Guid?>();
        pending.Enqueue(null); // null == the tree root level
        var seenParents = new HashSet<Guid>();

        while (pending.Count > 0 && results.Count < maxResults)
        {
            var parentId = pending.Dequeue();
            for (var skip = 0; ; )
            {
                var page = await fetchPage(parentId, skip, pageSize, ct);
                if (page.Count == 0)
                    break;

                foreach (var (id, descend, include, item) in page)
                {
                    if (include)
                        results.Add(item);
                    if (descend && seenParents.Add(id))
                        pending.Enqueue(id);
                }

                // A short page is the last one; otherwise advance and keep paging.
                if (page.Count < pageSize)
                    break;
                skip += page.Count;
            }
        }

        return results;
    }

    /// <summary>
    /// <see cref="CollectTreeAsync{T}"/> for a folder tree (#97): folders are descended into and
    /// left out, so a <c>list</c> gets every real type - including those nested inside folders,
    /// which the tree root omits - and never a folder container, whose id 404s on <c>get</c>.
    /// </summary>
    /// <typeparam name="T">The mapped item type.</typeparam>
    /// <param name="fetchPage">Fetches a page as <c>(id, isFolder, mappedItem)</c> for a parent folder (null = root).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every non-folder item in the tree, in breadth-first order.</returns>
    private static Task<List<T>> CollectTreeLeavesAsync<T>(
        Func<
            Guid?,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<(Guid Id, bool IsFolder, T Item)>>
        > fetchPage,
        CancellationToken ct
    ) =>
        CollectTreeAsync<T>(
            async (parent, skip, take, c) =>
                [
                    .. (await fetchPage(parent, skip, take, c)).Select(i =>
                        (i.Id, i.IsFolder, !i.IsFolder, i.Item)
                    ),
                ],
            ct
        );

    /// <summary>The ids of every non-folder item in a folder tree, for the alias resolvers.</summary>
    /// <param name="fetchPage">Fetches a page: (parent folder id or null for root, skip, take, ct).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids of all non-folder items in the tree.</returns>
    private static Task<List<Guid>> CollectTreeLeafIdsAsync(
        Func<
            Guid?,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<(Guid Id, bool IsFolder)>>
        > fetchPage,
        CancellationToken ct
    ) =>
        CollectTreeLeavesAsync<Guid>(
            async (parent, skip, take, c) =>
                [.. (await fetchPage(parent, skip, take, c)).Select(i => (i.Id, i.IsFolder, i.Id))],
            ct
        );

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
                var documentTypeId = await IdOfAsync(EntityKind.DocumentType, reference, ct);

                // Honour a client-supplied id for an idempotent create (#140); generate one
                // otherwise. Either way the id is known, so the empty 201 body can be hydrated.
                var id = request.Id ?? Guid.NewGuid();

                // A variant that names no culture gets the default language when the type
                // varies by culture (#228); an invariant type keeps the null culture.
                var variants = MapVariants(request.Variants);
                await DefaultVariantCulturesAsync(variants, documentTypeId, ct);

                // CreateDocumentBody (not the raw generated model) so that `template` is
                // always serialized: Umbraco 17+ requires the property to be present on a
                // document-create body, but Kiota omits a null complex property. See #134.
                var body = new CreateDocumentBody
                {
                    Id = id,
                    DocumentType = new Gen.ReferenceByIdModel { Id = documentTypeId },
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                    Variants = variants,
                    Values = MapValues(request.Values),
                    // Null is meaningful here: Umbraco 17 reads an explicit null template as
                    // "use the document type's default" (#134/#162), so only set it when asked.
                    Template = request.Template is { } t
                        ? new Gen.ReferenceByIdModel { Id = await TemplateIdAsync(t, ct) }
                        : null,
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
    /// A <see cref="Gen.CreateDocumentRequestModel"/> that always serializes the
    /// <c>template</c> property, even when it is null. The Umbraco 17+ Management API marks
    /// <c>template</c> as <b>required</b> on document create (it is nullable - a document may
    /// have no template), but Kiota omits null complex properties from the request body, so a
    /// create with no template was rejected with HTTP 400 ("missing required properties
    /// including: 'template'"). Forcing the key to be written keeps document create working
    /// across Umbraco 14-18. See issue #134.
    /// </summary>
    internal sealed class CreateDocumentBody : Gen.CreateDocumentRequestModel
    {
        /// <summary>
        /// Serializes the model, then writes <c>"template": null</c> when no template is set,
        /// because the base serializer omits the null complex property and v17 rejects a body
        /// without the key. When a template <i>is</i> set the base serializer writes it and
        /// this adds nothing.
        /// </summary>
        /// <param name="writer">The Kiota serialization writer.</param>
        public override void Serialize(ISerializationWriter writer)
        {
            base.Serialize(writer);
            if (Template is null)
                writer.WriteNullValue("template");
        }
    }

    /// <summary>
    /// Maps command-facing variants to the generated document variant shape. Shared by the document
    /// (content) and document-blueprint write paths, which use the same variant contract.
    /// </summary>
    /// <param name="variants">The command-facing variants.</param>
    /// <returns>The generated variant models.</returns>
    private static List<Gen.DocumentVariantRequestModel> MapVariants(
        IEnumerable<ContentVariant> variants
    ) =>
        variants
            .Select(v => new Gen.DocumentVariantRequestModel
            {
                Name = v.Name,
                Culture = v.Culture,
                Segment = v.Segment,
            })
            .ToList();

    /// <summary>
    /// Maps command-facing values to the generated document value shape, converting each JSON value
    /// to an <c>UntypedNode</c>. Shared by the document (content) and document-blueprint write paths.
    /// </summary>
    /// <param name="values">The command-facing values.</param>
    /// <returns>The generated value models.</returns>
    private static List<Gen.DocumentValueModel> MapValues(IEnumerable<ContentValue> values) =>
        values
            .Select(cv => new Gen.DocumentValueModel
            {
                Alias = cv.Alias,
                Culture = cv.Culture,
                Segment = cv.Segment,
                Value = UntypedNodeFactory.FromValue(cv.Value),
            })
            .ToList();

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
    /// <para>
    /// Two things about this endpoint are not obvious, both established by testing against
    /// 17.7.0 (#158). First, an empty <c>schedule</c> object is not "publish now" - Umbraco
    /// answers <c>200</c> and publishes nothing - so the schedule is sent only when a time was
    /// actually asked for. Second, <c>"*"</c> is <b>not</b> a wildcard: it is the invariant
    /// culture, so on a document that varies by culture it is rejected with
    /// <c>400 "Cannot publish invariant culture when the document varies by culture."</c>
    /// That is why publishing "everything" reads the document first and enumerates its cultures
    /// rather than relying on a wildcard that does not exist.
    /// </para>
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes every culture the document has.</param>
    /// <param name="publishAt">When to publish; null publishes immediately.</param>
    /// <param name="unpublishAt">When to take it down again; null leaves it published.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        DateTimeOffset? publishAt = null,
        DateTimeOffset? unpublishAt = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var requested = cultures?.Select(c => (string?)c).ToList();
                var targets = requested is { Count: > 0 }
                    ? requested
                    : await DocumentCulturesAsync(id, ct);

                // A schedule is only sent when one was asked for: an all-null schedule object is
                // silently ignored by the server, which is the #158 no-op.
                var schedule =
                    publishAt is null && unpublishAt is null
                        ? null
                        : new Gen.ScheduleRequestModel
                        {
                            PublishTime = publishAt,
                            UnpublishTime = unpublishAt,
                        };

                var body = new Gen.PublishDocumentRequestModel
                {
                    PublishSchedules = targets
                        .Select(c => new Gen.CultureAndScheduleRequestModel
                        {
                            Culture = c,
                            Schedule = schedule,
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
    /// The unpublish payload is a plain list of cultures (distinct from publish's schedule list).
    /// <para>
    /// When no cultures are given, the document is read and the call mirrors publish (#235): a
    /// document that varies by culture gets every culture it has listed, because Umbraco 17.7
    /// rejects a culture-less body on it with <c>400 "Cannot publish invariant culture when the
    /// document varies by culture."</c>. An invariant document gets the <c>cultures</c> field
    /// omitted, which is how it is unpublished whole (#149). <c>"*"</c> is never sent: it is the
    /// invariant culture, not a wildcard (#158).
    /// </para>
    /// </summary>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">Specific cultures to unpublish; null/empty unpublishes every culture the document has.</param>
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
                var requested = cultures?.ToList();
                if (requested is not { Count: > 0 })
                {
                    // Nothing named: a variant document needs its cultures listed, an invariant
                    // one needs the field omitted ([null] from the helper means invariant).
                    var targets = await DocumentCulturesAsync(id, ct);
                    requested = targets[0] is null ? null : targets.Select(c => c!).ToList();
                }

                var body = new Gen.UnpublishDocumentRequestModel { Cultures = requested };
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
    /// <para>
    /// Umbraco returns no versions for a document that varies by culture unless a culture is
    /// passed (#209), which read as "no history". So when no culture is named the document is
    /// read first: an invariant one gets the single unfiltered query, a variant one gets a query
    /// per culture and the results are merged newest first. Every row carries the culture it was
    /// listed under (null when invariant), which is also the <c>--culture</c> that
    /// <c>content rollback</c> needs for that version.
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
                List<string?> cultures = !string.IsNullOrEmpty(culture)
                    ? [culture]
                    : await DocumentCulturesAsync(documentId, ct);

                // One culture (named, or the invariant null) pages on the server as before.
                if (cultures.Count == 1)
                    return await VersionsPageAsync(documentId, cultures[0], skip, take, ct);

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
                        .OrderByDescending(v => v.VersionDate)
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
                .ToList(),
        };
    }

    /// <summary>
    /// Reads one version of a document, with its values, via <c>GET document-version/{id}</c>
    /// (#209). The body is returned raw so every property value survives, which is what a
    /// diff before <c>content rollback</c> needs.
    /// </summary>
    /// <param name="versionId">The version id, from <see cref="GetDocumentVersionsAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version body as JSON, or a mapped failure.</returns>
    public Task<UmbracoResponse<JsonNode>> GetDocumentVersionAsync(
        Guid versionId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => GetRawJsonAsync($"umbraco/management/api/v1/document-version/{versionId}", ct)
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

    /// <summary>
    /// Copies a document under a new parent via <c>POST document/{id}/copy</c> (issue #67) and
    /// surfaces the copy's new id (issue #91). The endpoint returns <c>201 Created</c> with the new
    /// id in the <c>Location</c> header and an empty body; unlike the other creates the server
    /// assigns the id, so it cannot be pre-supplied. The generated method discards the response, so
    /// a Kiota <see cref="NativeResponseHandler"/> captures the raw response to read the
    /// <c>Location</c>; the new node is then best-effort hydrated so a script can chain to the copy.
    /// </summary>
    /// <param name="id">The document id to copy.</param>
    /// <param name="parentId">Target parent id; null copies to the content root.</param>
    /// <param name="includeDescendants">Whether to copy descendants too.</param>
    /// <param name="relateToOriginal">Whether to create a relation to the original.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The copied document (with its new id), or a mapped failure.</returns>
    public async Task<UmbracoResponse<ContentItemResponse>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    )
    {
        var body = new Gen.CopyDocumentRequestModel
        {
            Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
            IncludeDescendants = includeDescendants,
            RelateToOriginal = relateToOriginal,
        };
        return await CopyViaLocationAsync(
            config => _api.Umbraco.Management.Api.V1.Document[id].Copy.PostAsync(body, config, ct),
            newId => GetContentByIdAsync(newId, ct),
            newId => new ContentItemResponse { Id = newId },
            "document",
            ct
        );
    }

    /// <summary>
    /// The shared shape of a copy (#91, #247): the server assigns the copy's id and returns it only
    /// in the <c>201</c> <c>Location</c> header, which the generated methods throw away, so a
    /// <see cref="NativeResponseHandler"/> captures the raw response. A 201 with no usable
    /// <c>Location</c> is reported as a failure rather than a silent empty-id success, since the
    /// point of a copy's output is the new id. The new item is then re-read for a full result; a
    /// failed read still reports success with the id (the copy itself succeeded).
    /// </summary>
    /// <typeparam name="T">The copied item's response type.</typeparam>
    /// <param name="post">Sends the copy, applying the given request configuration.</param>
    /// <param name="hydrate">Re-reads the new item by id.</param>
    /// <param name="fromId">The id-only result when the re-read fails.</param>
    /// <param name="noun">The item kind, for the no-Location message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The copy, or a mapped failure.</returns>
    private static async Task<UmbracoResponse<T>> CopyViaLocationAsync<T>(
        Func<Action<RequestConfiguration<DefaultQueryParameters>>, Task> post,
        Func<Guid, Task<UmbracoResponse<T>>> hydrate,
        Func<Guid, T> fromId,
        string noun,
        CancellationToken ct
    )
    {
        var copied = await GuardedApiAsync<Guid?>(
            ct,
            async () =>
            {
                var capture = new NativeResponseHandler();
                await post(config =>
                    config.Options.Add(new ResponseHandlerOption { ResponseHandler = capture })
                );
                return await CreatedIdAsync(capture, ct);
            }
        );

        if (!copied.IsSuccess)
            return UmbracoResponse<T>.FailureFrom(copied);
        if (copied.Data is not { } newId)
            return UmbracoResponse<T>.Failure(
                502,
                $"The {noun} was copied but the server did not return the new id (no Location header)."
            );

        var hydrated = await hydrate(newId);
        return hydrated is { IsSuccess: true, Data: { } data }
            ? UmbracoResponse<T>.Success(data)
            : UmbracoResponse<T>.Success(fromId(newId));
    }

    /// <summary>
    /// Reads the new resource id from a captured <c>201</c> response's <c>Location</c> header (issue
    /// #91): the id is the last path segment. Returns null when there is no location or it is not a GUID.
    /// <para>
    /// Attaching a <see cref="NativeResponseHandler"/> makes Kiota hand the response over as it is,
    /// <b>skipping its error mapping</b>, so a rejected request would otherwise come back as "created,
    /// but no Location". A non-success status is therefore raised here as the
    /// <see cref="ApiException"/> the error mapping would have raised, carrying the status and the
    /// problem-details <c>detail</c>/<c>title</c>, for <see cref="GuardedApiAsync{T}"/> to map.
    /// </para>
    /// </summary>
    /// <param name="capture">The native response handler that captured the raw HTTP response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new resource id, or null when it cannot be read.</returns>
    /// <exception cref="ApiException">The response was not a success.</exception>
    private static async Task<Guid?> CreatedIdAsync(
        NativeResponseHandler capture,
        CancellationToken ct
    )
    {
        if (capture.Value is not HttpResponseMessage response)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new ApiException(await ProblemMessageAsync(response, ct))
            {
                ResponseStatusCode = (int)response.StatusCode,
            };
        var lastSegment = response
            .Headers.Location?.OriginalString.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return Guid.TryParse(lastSegment, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// The <c>detail</c> (else <c>title</c>) of a problem-details error body, or an empty string when
    /// the body has neither - <see cref="DescribeApiException"/> turns an empty message into a
    /// readable one for the status.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server's message, or an empty string.</returns>
    private static async Task<string> ProblemMessageAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            return (string?)body?["detail"] ?? (string?)body?["title"] ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
        catch (InvalidOperationException)
        {
            // A detail/title that is not a string.
            return "";
        }
    }

    /// <summary>How often <c>--wait</c> polls the publish-with-descendants task (issue #90).</summary>
    private static readonly TimeSpan PublishDescendantsPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long <c>--wait</c> polls before giving up and returning the last-known state (issue #90).</summary>
    private static readonly TimeSpan PublishDescendantsPollTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Publishes a document and its descendants via
    /// <c>PUT document/{id}/publish-with-descendants</c> (issue #67). The server runs the branch
    /// publish as a background task, returning a task id and a completion flag; with
    /// <paramref name="wait"/> the call polls
    /// <c>GET document/{id}/publish-with-descendants/result/{taskId}</c> until it completes or the
    /// poll timeout elapses (issue #90).
    /// </summary>
    /// <param name="id">The root document id.</param>
    /// <param name="cultures">Cultures to publish; null/empty publishes all (<c>*</c>).</param>
    /// <param name="includeUnpublishedDescendants">Whether to also publish never-published descendants.</param>
    /// <param name="wait">Whether to poll the background task to completion before returning.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task id and completion state, or a mapped failure.</returns>
    public Task<UmbracoResponse<PublishDescendantsResult>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        bool wait = false,
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
                var started = await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .PublishWithDescendants.PutAsync(body, cancellationToken: ct);

                var taskId = started?.TaskId;
                var complete = started?.IsComplete ?? false;

                // #90: poll the result endpoint until the task reports complete (or we time out).
                // A synchronously-complete server, or a missing task id, needs no polling.
                if (wait && !complete && taskId is { } tid)
                    complete = await PollPublishDescendantsAsync(id, tid, ct);

                return new PublishDescendantsResult { TaskId = taskId, IsComplete = complete };
            }
        );

    /// <summary>
    /// Polls the publish-with-descendants result endpoint until the task completes or
    /// <see cref="PublishDescendantsPollTimeout"/> elapses (issue #90).
    /// </summary>
    /// <param name="id">The root document id.</param>
    /// <param name="taskId">The background task id to poll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the task completed within the timeout; false if it timed out still running.</returns>
    private async Task<bool> PollPublishDescendantsAsync(Guid id, Guid taskId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + PublishDescendantsPollTimeout;
        while (true)
        {
            var result = await _api
                .Umbraco.Management.Api.V1.Document[id]
                .PublishWithDescendants.Result[taskId]
                .GetAsync(cancellationToken: ct);
            if (result?.IsComplete ?? false)
                return true;
            if (DateTimeOffset.UtcNow >= deadline)
                return false;
            await Task.Delay(PublishDescendantsPollInterval, ct);
        }
    }

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

                // The media type's alias and the item's URLs are two independent follow-up reads.
                // Started together rather than awaited inside the initializer below, where the
                // ordering would be invisible and they would run one after the other.
                var aliasTask = m?.MediaType?.Id is { } typeId
                    ? MediaTypeAliasAsync(typeId, ct)
                    : Task.FromResult<string?>(null);
                var urlsTask = MediaUrlsAsync(id, ct);
                await Task.WhenAll(aliasTask, urlsTask);

                return new MediaItemResponse
                {
                    Id = m?.Id ?? id,
                    Name = variant?.Name ?? "",
                    // The real alias (#222; this used to be the name, because upload matched names
                    // only). Upload now takes the alias or the name, so the value round-trips.
                    MediaType = m?.MediaType?.Id is { } mtId
                        ? new ContentTypeRef { Id = mtId, Alias = aliasTask.Result }
                        : null,
                    CreateDate = variant?.CreateDate ?? default,
                    UpdateDate = variant?.UpdateDate ?? default,
                    // #172: width, height, bytes and extension are all in values[]; they were
                    // being fetched and thrown away on every read.
                    Values = MapMediaValueResponses(m?.Values),
                    Urls = urlsTask.Result,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<MediaItemResponse>> CreateMediaFolderAsync(
        string name,
        Guid? parentId = null,
        Guid? id = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // "Folder" is Umbraco's built-in folder media type, resolved by name like every
                // other media type reference (the shared resolver takes an alias or a name).
                var folderTypeId = await IdOfAsync(EntityKind.MediaType, "Folder", ct);
                var folderId = id ?? Guid.NewGuid();

                await _api.Umbraco.Management.Api.V1.Media.PostAsync(
                    new Gen.CreateMediaRequestModel
                    {
                        Id = folderId,
                        MediaType = new Gen.ReferenceByIdModel { Id = folderTypeId },
                        Parent = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                        Variants = [new Gen.MediaVariantRequestModel { Name = name }],
                        // A folder holds no file, so it carries no values at all.
                        Values = [],
                    },
                    cancellationToken: ct
                );

                // The create response is empty; re-read so the caller gets the stored item rather
                // than an echo of the request (#172's lesson).
                var hydrated = await GetMediaByIdAsync(folderId, ct);
                if (hydrated.IsSuccess && hydrated.Data is { } stored)
                    return string.IsNullOrEmpty(stored.Name) ? stored with { Name = name } : stored;
                return new MediaItemResponse { Id = folderId, Name = name };
            }
        );

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
    /// <param name="id">The id to create the item with (#226); null generates one.</param>
    /// <param name="values">Property values to set besides the file (#220), sent after <c>umbracoFile</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media item (id + echoed name), or a mapped failure.</returns>
    public Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid? parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        string mediaType,
        Guid? id = null,
        IReadOnlyList<MediaValue>? values = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Step 1: resolve the media type to an id (GUID passthrough, else name search).
                var mediaTypeId = await IdOfAsync(EntityKind.MediaType, mediaType, ct);

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
                // A caller-supplied id keeps the item's GUID the same on every instance, which is
                // what content referencing it by GUID needs to survive a promotion (#226).
                var mediaId = id ?? Guid.NewGuid();
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
                        // #220: a media type with required fields cannot be uploaded to without
                        // them, and there is no media update to set them afterwards.
                        .. (values ?? []).Select(v => new Gen.MediaValueModel
                        {
                            Alias = v.Alias,
                            Culture = v.Culture,
                            Segment = v.Segment,
                            Value = UntypedNodeFactory.FromValue(v.Value),
                        }),
                    ],
                };
                await _api.Umbraco.Management.Api.V1.Media.PostAsync(body, cancellationToken: ct);

                // The create response is empty; the id is the client-generated one and the name is
                // echoed so the command reports a populated item rather than a blank one (#74).
                // #172: the create response is empty, so this used to echo back a fabricated item
                // carrying only the id and name the caller already had. Re-read instead, so the
                // caller gets the URL, dimensions and size of what was actually stored.
                //
                // The re-read is best-effort in both directions: a failed read must not fail an
                // upload that succeeded, and a read that comes back blank must not overwrite what
                // we already know. So the supplied name stands in whenever the re-read has none
                // (#74 - the returned item is never blanked).
                var hydrated = await GetMediaByIdAsync(mediaId, ct);
                if (hydrated.IsSuccess && hydrated.Data is { } stored)
                    return string.IsNullOrEmpty(stored.Name) ? stored with { Name = name } : stored;
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
    /// id/name/icon, so the alias costs one by-id read per type, cached for the client's life.
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
                // #97: every real type (folders excluded, nested types included), paged
                // client-side; #221: with its alias, which was always "".
                var all = await MediaTypesWithAliasAsync(ct);
                return new PagedResponse<MediaTypeResponse>
                {
                    Total = all.Count,
                    Items = all.Skip(skip).Take(take).ToList(),
                };
            }
        );

    /// <summary>
    /// Fetches one page of the media-type tree as <c>(id, isFolder, item)</c>: the root level when
    /// <paramref name="parentId"/> is null, otherwise the children of that folder.
    /// </summary>
    /// <param name="parentId">The parent folder id, or null for the tree root.</param>
    /// <param name="s">Items to skip.</param>
    /// <param name="t">Page size.</param>
    /// <param name="c">Cancellation token.</param>
    /// <returns>The page.</returns>
    private async Task<
        IReadOnlyList<(Guid Id, bool IsFolder, MediaTypeResponse Item)>
    > FetchMediaTypeTreeAsync(Guid? parentId, int s, int t, CancellationToken c)
    {
        var items = parentId is null
            ? (
                await _api.Umbraco.Management.Api.V1.Tree.MediaType.Root.GetAsync(
                    q =>
                    {
                        q.QueryParameters.Skip = s;
                        q.QueryParameters.Take = t;
                    },
                    c
                )
            )?.Items
            : (
                await _api.Umbraco.Management.Api.V1.Tree.MediaType.Children.GetAsync(
                    q =>
                    {
                        q.QueryParameters.ParentId = parentId;
                        q.QueryParameters.Skip = s;
                        q.QueryParameters.Take = t;
                    },
                    c
                )
            )?.Items;
        return
        [
            .. (items ?? [])
                .Where(i => i.Id is not null)
                .Select(i =>
                    (
                        i.Id!.Value,
                        i.IsFolder ?? false,
                        new MediaTypeResponse
                        {
                            Id = i.Id!.Value,
                            Name = i.Name ?? "",
                            Icon = i.Icon,
                        }
                    )
                ),
        ];
    }

    /// <summary>
    /// Fetches one page of the member-type tree as <c>(id, isFolder, item)</c>: the root level
    /// when <paramref name="parentId"/> is null, otherwise the children of that folder.
    /// </summary>
    /// <param name="parentId">The parent folder id, or null for the tree root.</param>
    /// <param name="s">Items to skip.</param>
    /// <param name="t">Page size.</param>
    /// <param name="c">Cancellation token.</param>
    /// <returns>The page.</returns>
    private async Task<
        IReadOnlyList<(Guid Id, bool IsFolder, MemberTypeResponse Item)>
    > FetchMemberTypeTreeAsync(Guid? parentId, int s, int t, CancellationToken c)
    {
        var items = parentId is null
            ? (
                await _api.Umbraco.Management.Api.V1.Tree.MemberType.Root.GetAsync(
                    q =>
                    {
                        q.QueryParameters.Skip = s;
                        q.QueryParameters.Take = t;
                    },
                    c
                )
            )?.Items
            : (
                await _api.Umbraco.Management.Api.V1.Tree.MemberType.Children.GetAsync(
                    q =>
                    {
                        q.QueryParameters.ParentId = parentId;
                        q.QueryParameters.Skip = s;
                        q.QueryParameters.Take = t;
                    },
                    c
                )
            )?.Items;
        return
        [
            .. (items ?? [])
                .Where(i => i.Id is not null)
                .Select(i =>
                    (
                        i.Id!.Value,
                        i.IsFolder ?? false,
                        new MemberTypeResponse
                        {
                            Id = i.Id!.Value,
                            Name = i.Name ?? "",
                            Icon = i.Icon,
                        }
                    )
                ),
        ];
    }

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
                // #97: the document-type tree groups types into folders whose ids 404 on `get`.
                // Walk the whole tree, keeping only real types (folders excluded, nested types
                // included), then page client-side so Total and Items agree.
                var all = await CollectTreeLeavesAsync<DocumentTypeResponse>(
                    async (parentId, s, t, c) =>
                    {
                        var items = parentId is null
                            ? (
                                await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Root.GetAsync(
                                    q =>
                                    {
                                        q.QueryParameters.Skip = s;
                                        q.QueryParameters.Take = t;
                                    },
                                    c
                                )
                            )?.Items
                            : (
                                await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Children.GetAsync(
                                    q =>
                                    {
                                        q.QueryParameters.ParentId = parentId;
                                        q.QueryParameters.Skip = s;
                                        q.QueryParameters.Take = t;
                                    },
                                    c
                                )
                            )?.Items;
                        return
                        [
                            .. (items ?? [])
                                .Where(i => i.Id is not null)
                                .Select(i =>
                                    (
                                        i.Id!.Value,
                                        i.IsFolder ?? false,
                                        new DocumentTypeResponse
                                        {
                                            Id = i.Id!.Value,
                                            Name = i.Name ?? "",
                                            IsElement = i.IsElement ?? false,
                                        }
                                    )
                                ),
                        ];
                    },
                    ct
                );
                return new PagedResponse<DocumentTypeResponse>
                {
                    Total = all.Count,
                    Items = all.Skip(skip).Take(take).ToList(),
                };
            }
        );

    /// <summary>
    /// Gets a single document type by id via <c>GET document-type/{id}</c> (generated client,
    /// #79). Unlike the tree/list view, the by-id response carries the alias, description and
    /// root-allowed flag.
    /// </summary>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document type mapped to <see cref="DocumentTypeResponse"/>.</returns>
    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => ReadDocumentTypeAsync(id, ct));

    /// <summary>Reads the by-id body and maps it, shared with the by-key lookup (#159).</summary>
    /// <param name="id">The type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The mapped type.</returns>
    private async Task<DocumentTypeResponse> ReadDocumentTypeAsync(Guid id, CancellationToken ct)
    {
        var dt = await _api
            .Umbraco.Management.Api.V1.DocumentType[id]
            .GetAsync(cancellationToken: ct);
        return new DocumentTypeResponse
        {
            Id = dt?.Id ?? id,
            Name = dt?.Name ?? "",
            Alias = dt?.Alias ?? "",
            Description = dt?.Description,
            IsElement = dt?.IsElement ?? false,
            AllowedAsRoot = dt?.AllowedAsRoot ?? false,
            Icon = dt?.Icon,
            VariesByCulture = dt?.VariesByCulture ?? false,
            VariesBySegment = dt?.VariesBySegment ?? false,
            // #160: the help text promised these for three releases while the mapping
            // dropped them, which is why authoring a property needed a schema round-trip.
            Properties = dt
                ?.Properties?.Select(p => new DocumentTypePropertyResponse
                {
                    Id = p.Id,
                    Alias = p.Alias ?? "",
                    Name = p.Name ?? "",
                    Description = p.Description,
                    DataType = p.DataType?.Id,
                    Container = p.Container?.Id,
                    SortOrder = p.SortOrder ?? 0,
                    VariesByCulture = p.VariesByCulture ?? false,
                    VariesBySegment = p.VariesBySegment ?? false,
                })
                .ToList(),
            Containers = dt
                ?.Containers?.Select(c => new DocumentTypeContainerResponse
                {
                    Id = c.Id,
                    Name = c.Name ?? "",
                    Type = c.Type,
                    SortOrder = c.SortOrder ?? 0,
                })
                .ToList(),
            Compositions = dt
                ?.Compositions?.Select(c => c.DocumentType?.Id)
                .Where(g => g is not null)
                .Select(g => g!.Value)
                .ToList(),
            AllowedTemplates = dt
                ?.AllowedTemplates?.Select(t => t.Id)
                .Where(g => g is not null)
                .Select(g => g!.Value)
                .ToList(),
            DefaultTemplate = dt?.DefaultTemplate?.Id,
        };
    }

    /// <summary>
    /// Creates a document type via <c>POST document-type</c> (generated client, #79). The id is
    /// client-supplied so the created type is echoed back with the accepted fields (the create
    /// response is empty). The CLI exposes only the scalar fields (name/alias/icon/description/
    /// element/root flags); properties, containers, compositions and allowed-type collections
    /// are sent empty, matching the command's surface.
    /// </summary>
    /// <param name="request">The document type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created document type (with the supplied id), or a mapped failure.</returns>
    public Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(
        CreateDocumentTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = new Gen.CreateDocumentTypeRequestModel
                {
                    Id = request.Id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Icon = request.Icon,
                    Description = request.Description,
                    IsElement = request.IsElement,
                    AllowedAsRoot = request.AllowedAsRoot,
                    VariesByCulture = request.VariesByCulture,
                    VariesBySegment = request.VariesBySegment,
                    Cleanup = new Gen.DocumentTypeCleanupModel
                    {
                        PreventCleanup = request.Cleanup.PreventCleanup,
                        KeepAllVersionsNewerThanDays = request.Cleanup.KeepAllVersionsNewerThanDays,
                        KeepLatestVersionPerDayForDays = request
                            .Cleanup
                            .KeepLatestVersionPerDayForDays,
                    },
                    Containers = [],
                    Properties = [],
                    AllowedDocumentTypes = [],
                    Compositions = [],
                    AllowedTemplates = request
                        .AllowedTemplates.Select(t => new Gen.ReferenceByIdModel { Id = t.Id })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.DocumentType.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return new DocumentTypeResponse
                {
                    Id = request.Id,
                    Name = request.Name,
                    Alias = request.Alias,
                    Description = request.Description,
                    IsElement = request.IsElement,
                    AllowedAsRoot = request.AllowedAsRoot,
                };
            }
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
    /// Lists data types from the data-type tree (issue #39 - there is no flat
    /// <c>/data-type</c> collection). Folders are organisational containers whose ids 404 on
    /// <c>data-type get</c>, so the tree is walked to return only real data types - folders
    /// excluded, types nested inside folders included - then paged client-side (#135, mirroring
    /// the #97 fix for content-types/media-types). The editor alias is not carried on tree
    /// items, so only id/name/editorUiAlias are populated for the list view.
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
                var all = await CollectTreeLeavesAsync<DataTypeResponse>(
                    async (parentId, s, t, c) =>
                    {
                        var items = parentId is null
                            ? (
                                await _api.Umbraco.Management.Api.V1.Tree.DataType.Root.GetAsync(
                                    q =>
                                    {
                                        q.QueryParameters.Skip = s;
                                        q.QueryParameters.Take = t;
                                    },
                                    c
                                )
                            )?.Items
                            : (
                                await _api.Umbraco.Management.Api.V1.Tree.DataType.Children.GetAsync(
                                    q =>
                                    {
                                        q.QueryParameters.ParentId = parentId;
                                        q.QueryParameters.Skip = s;
                                        q.QueryParameters.Take = t;
                                    },
                                    c
                                )
                            )?.Items;
                        return
                        [
                            .. (items ?? [])
                                .Where(i => i.Id is not null)
                                .Select(i =>
                                    (
                                        i.Id!.Value,
                                        i.IsFolder ?? false,
                                        new DataTypeResponse
                                        {
                                            Id = i.Id!.Value,
                                            Name = i.Name ?? "",
                                            EditorUiAlias = i.EditorUiAlias,
                                        }
                                    )
                                ),
                        ];
                    },
                    ct
                );
                // #176: the tree items carry only the editor UI alias, but editorAlias is what
                // tells a caller a property's value shape (#174) - the reason the test round
                // needed it. It is not on the tree, so the page is hydrated by id. Only the
                // requested page, never the whole tree, so the cost is bounded by --take.
                var page = all.Skip(skip).Take(take).ToList();

                // Concurrently, in bounded batches: serially this was one round trip per item,
                // so --take 100 meant 100 in a row.
                const int batchSize = 8;
                var hydrated = new List<DataTypeResponse>(page.Count);
                for (var i = 0; i < page.Count; i += batchSize)
                {
                    var batch = page.Skip(i).Take(batchSize).Select(item => HydrateAsync(item, ct));
                    hydrated.AddRange(await Task.WhenAll(batch));
                }

                return new PagedResponse<DataTypeResponse> { Total = all.Count, Items = hydrated };
            }
        );

    /// <summary>
    /// Gets a single data type by id via <c>GET data-type/{id}</c> (generated client, #79). The
    /// by-id response carries the editor aliases the tree/list view omits.
    /// </summary>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The data type mapped to <see cref="DataTypeResponse"/>.</returns>
    /// <summary>
    /// Reads a data type's full record for the list (#176), falling back to the tree's view when
    /// it cannot be read.
    /// <para>
    /// The fallback leaves <see cref="DataTypeResponse.EditorAlias"/> null rather than the tree's
    /// empty string, so "we could not read this" is distinguishable from "this genuinely has
    /// none" - in the one field the hydration exists to deliver, a silent downgrade would be
    /// worse than the gap it fills.
    /// </para>
    /// </summary>
    /// <param name="item">The tree's view of the data type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full record, or the tree's view with a null editor alias.</returns>
    private async Task<DataTypeResponse> HydrateAsync(DataTypeResponse item, CancellationToken ct)
    {
        try
        {
            return await ReadDataTypeAsync(item.Id, ct);
        }
        catch (ApiException)
        {
            return item with { EditorAlias = null };
        }
    }

    public Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => ReadDataTypeAsync(id, ct));

    /// <summary>Reads the by-id body and maps it, shared with the by-key lookup (#159).</summary>
    /// <param name="id">The type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The mapped type.</returns>
    private async Task<DataTypeResponse> ReadDataTypeAsync(Guid id, CancellationToken ct)
    {
        var dt = await _api.Umbraco.Management.Api.V1.DataType[id].GetAsync(cancellationToken: ct);
        return new DataTypeResponse
        {
            Id = dt?.Id ?? id,
            Name = dt?.Name ?? "",
            EditorAlias = dt?.EditorAlias ?? "",
            EditorUiAlias = dt?.EditorUiAlias,
            // #170: the editor configuration - a dropdown's items, a picker's filters.
            Values = dt
                ?.Values?.Select(v => new DataTypeValueResponse
                {
                    Alias = v.Alias ?? "",
                    Value = UntypedNodeFactory.ToJsonNode(v.Value),
                })
                .ToList(),
        };
    }

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
    /// Lists templates (issue #39 - there is no flat <c>/template</c> collection). Every template
    /// is listed, including those nested under a master (the tree root alone omits them), with
    /// its alias from the item endpoint (#206; it was always <c>""</c>). Paged client-side.
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
                var all = await TemplateCandidatesAsync(ct);
                return new PagedResponse<TemplateResponse>
                {
                    Total = all.Count,
                    Items =
                    [
                        .. all.Skip(skip)
                            .Take(take)
                            .Select(t => new TemplateResponse
                            {
                                Id = t.Id,
                                Name = t.Name ?? "",
                                Alias = t.Alias ?? "",
                            }),
                    ],
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
                var id = await IdOfAsync(EntityKind.Template, aliasOrId, ct);

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
                        // #184: MemberGroupName, not Filter. `filter` is the free-text search
                        // over name and email, so filtering by group matched nothing and
                        // returned an empty list with exit 0 - a silent wrong answer.
                        if (!string.IsNullOrEmpty(group))
                            c.QueryParameters.MemberGroupName = group;
                    },
                    ct
                );
                return new PagedResponse<MemberResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = await LabelMembersAsync(
                        [.. (paged?.Items ?? []).Select(MapMember)],
                        ct
                    ),
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
                return m is null
                    ? new MemberResponse { Id = id }
                    : await LabelMemberAsync(MapMember(m), ct);
            }
        );

    /// <summary>
    /// Member-type tree leaf ids (folders excluded), captured on the first alias resolution.
    /// </summary>
    private List<Guid>? _memberTypeLeafIds;

    /// <summary>Member-type alias to id, filled in as candidates are read by-id.</summary>
    private readonly Dictionary<string, Guid> _memberTypeAliases = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Member-type ids already read by-id, so a candidate is never fetched twice.</summary>
    private readonly HashSet<Guid> _memberTypeAliasesRead = [];

    /// <summary>
    /// Resolves a member-type reference - an alias or a GUID id - to its id, mirroring
    /// <see cref="FindDocumentTypeIdAsync"/>: a GUID is used directly, otherwise the member-type
    /// tree is walked and each candidate read by-id to compare its alias. As with document types
    /// the item search is deliberately avoided - it indexes names, not aliases. See ADR 0004.
    /// </summary>
    /// <param name="aliasOrId">The member-type alias or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved member-type id.</returns>
    /// <exception cref="ApiException">No member type matches the alias (mapped to a 404).</exception>
    private async Task<Guid> FindMemberTypeIdAsync(string aliasOrId, CancellationToken ct)
    {
        if (_memberTypeAliases.TryGetValue(aliasOrId, out var cached))
            return cached;

        _memberTypeLeafIds ??= await CollectTreeLeafIdsAsync(FetchMemberTypeTreePageAsync, ct);

        foreach (var candidateId in _memberTypeLeafIds)
        {
            if (!_memberTypeAliasesRead.Add(candidateId))
                continue;

            var mt = await _api
                .Umbraco.Management.Api.V1.MemberType[candidateId]
                .GetAsync(cancellationToken: ct);
            if (mt?.Alias is { } alias)
                _memberTypeAliases[alias] = candidateId;
            if (string.Equals(mt?.Alias, aliasOrId, StringComparison.OrdinalIgnoreCase))
                return candidateId;
        }

        throw NotFound(
            $"No member type found with alias '{aliasOrId}'. Use 'umbraco member-types list' "
                + "to find one, or pass a member type id."
        );
    }

    /// <summary>
    /// Fetches one page of the member-type tree: the root level when <paramref name="parentId"/>
    /// is null, otherwise the children of that folder.
    /// </summary>
    /// <param name="parentId">The parent folder id, or null for the tree root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's items as (id, is-folder) pairs.</returns>
    private async Task<IReadOnlyList<(Guid Id, bool IsFolder)>> FetchMemberTypeTreePageAsync(
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        var items = parentId is null
            ? (
                await _api.Umbraco.Management.Api.V1.Tree.MemberType.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                )
            )?.Items?.Select(i => (i.Id, i.IsFolder))
            : (
                await _api.Umbraco.Management.Api.V1.Tree.MemberType.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parentId;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                )
            )?.Items?.Select(i => (i.Id, i.IsFolder));

        return
        [
            .. (items ?? [])
                .Where(i => i.Id is not null)
                .Select(i => (i.Id!.Value, i.IsFolder ?? false)),
        ];
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
                var memberTypeId = await IdOfAsync(EntityKind.MemberType, reference, ct);

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
                    MemberType = new ContentTypeRef { Id = memberTypeId },
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

                // Values are merged by alias + culture + segment rather than replaced, so
                // setting one property does not clear the rest (#179's rule, applied here too).
                var values = MergeByKey.Upsert(
                    (m.Values ?? []).Select(v => new Gen.MemberValueModel
                    {
                        Alias = v.Alias,
                        Culture = v.Culture,
                        Segment = v.Segment,
                        Value = v.Value,
                    }),
                    (request.Values ?? []).Select(v => new Gen.MemberValueModel
                    {
                        Alias = v.Alias,
                        Culture = v.Culture,
                        Segment = v.Segment,
                        Value = UntypedNodeFactory.FromValue(v.Value),
                    }),
                    v => (v.Alias, v.Culture, v.Segment)
                );

                var body = new Gen.UpdateMemberRequestModel
                {
                    Email = request.Email ?? m.Email ?? "",
                    Username = request.Username ?? m.Username ?? "",
                    IsApproved = request.IsApproved ?? m.IsApproved ?? false,
                    // Unlocking is a real operation; locking a member out by hand is not, so this
                    // only ever goes false deliberately.
                    IsLockedOut = request.IsLockedOut ?? m.IsLockedOut ?? false,
                    IsTwoFactorEnabled = m.IsTwoFactorEnabled ?? false,
                    // Groups replace wholesale when supplied - a group list is the membership,
                    // not a patch - and are preserved untouched when they are not.
                    Groups = request.Groups is { } g
                        ? g.Select(x => (Guid?)x).ToList()
                        : (m.Groups ?? []).ToList(),
                    // Only ever set deliberately. Kiota omits a null string from the body, so a
                    // member's password survives an update that does not mention it - asserted
                    // on the wire rather than assumed, because the cost of being wrong is
                    // locking a member out of a live site.
                    NewPassword = string.IsNullOrEmpty(request.NewPassword)
                        ? null
                        : request.NewPassword,
                    Values = values,
                    Variants = variants,
                };
                await _api
                    .Umbraco.Management.Api.V1.Member[id]
                    .PutAsync(body, cancellationToken: ct);

                // Echo the merged member (the PUT returns no body).
                var mapped = await LabelMemberAsync(MapMember(m), ct);
                return mapped with
                {
                    Email = request.Email ?? mapped.Email,
                    Name = request.Name ?? mapped.Name,
                    IsApproved = request.IsApproved ?? mapped.IsApproved,
                };
            }
        );

    /// <summary>
    /// Deletes a member via <c>DELETE member/{id}</c> (generated client).
    /// </summary>
    /// <remarks>
    /// Umbraco 17.x returns an undeclared HTTP 500 from this endpoint even when the member IS
    /// removed (the OpenAPI spec only declares 200/400/404, so the generated client has no error
    /// factory for 500 and throws "no error factory is registered for this code: 500"). Reporting
    /// that as a failure breaks callers and teardown scripts that then treat a successful delete as
    /// a failure. So on a 500 we confirm the post-condition with a follow-up read: if the member is
    /// gone the delete succeeded; only a member that still exists is a genuine failure we surface.
    /// A side effect is that deleting an already-absent member reports success (idempotent delete),
    /// which is the friendlier behaviour for scripts (#150, found in alpha.8 acceptance testing).
    /// </remarks>
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
                try
                {
                    await _api
                        .Umbraco.Management.Api.V1.Member[id]
                        .DeleteAsync(cancellationToken: ct);
                    return Empty.Value;
                }
                catch (ApiException ex) when (ex.ResponseStatusCode == 500)
                {
                    // Undeclared 500 from a delete that may well have succeeded - verify.
                    if (await MemberExistsAsync(id, ct))
                        throw; // still there: a real 500, let the guard surface it
                    return Empty.Value; // gone: the delete worked despite the 500
                }
            }
        );

    /// <summary>
    /// Probes whether a member still exists, used to confirm a delete whose response was an
    /// undeclared HTTP 500 (see <see cref="DeleteMemberAsync"/>). Only a definitive 404 from the
    /// read proves the member is gone; a returned member, or any inconclusive error, is treated as
    /// "still exists" so the original failure is surfaced rather than masked.
    /// </summary>
    /// <param name="id">The member id to probe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the member still exists or existence could not be disproven.</returns>
    private async Task<bool> MemberExistsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var member = await _api
                .Umbraco.Management.Api.V1.Member[id]
                .GetAsync(cancellationToken: ct);
            return member is not null;
        }
        // A 404 arrives as the generated ProblemDetails (a declared error body); ResponseStatusCode
        // can be 0 on it, so fall back to the parsed Status - matching GuardedApiAsync.
        catch (Gen.ProblemDetails pd)
            when ((pd.ResponseStatusCode != 0 ? pd.ResponseStatusCode : pd.Status ?? 0) == 404)
        {
            return false;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return false;
        }
    }

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
                // #213: every member type, folders walked, with its alias; paged client-side.
                var all = await MemberTypesWithAliasAsync(ct);
                return new PagedResponse<MemberTypeResponse>
                {
                    Total = all.Count,
                    Items = all.Skip(skip).Take(take).ToList(),
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

    /// <summary>
    /// Updates a member type via a raw-JSON read-merge (issue #56). The member-type PUT is a
    /// full replace whose typed model would drop the type's properties, containers and
    /// compositions (the response and request models use different element types for those), so
    /// this reads the current type as verbatim JSON, patches only the supplied scalar fields,
    /// and writes the whole document back - the same lossless round-trip the schema pipeline
    /// uses (ADR 0005). The PUT flows through the intercepted <see cref="HttpClient"/>, so
    /// <c>--dry-run</c> previews it like any other mutation.
    /// </summary>
    /// <param name="id">The member type id.</param>
    /// <param name="request">The fields to change; null fields keep their current value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> UpdateMemberTypeAsync(
        Guid id,
        UpdateMemberTypeRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () =>
                UpdateRawScalarsAsync(
                    $"umbraco/management/api/v1/member-type/{id}",
                    new Dictionary<string, string?>
                    {
                        ["name"] = request.Name,
                        ["alias"] = request.Alias,
                        ["description"] = request.Description,
                        ["icon"] = request.Icon,
                    },
                    ct
                )
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

    /// <inheritdoc />
    public Task<UmbracoResponse<int>> CountMembersOfTypeAsync(
        Guid memberTypeId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // One item is enough: only the page's total is read.
                var paged = await _api.Umbraco.Management.Api.V1.Filter.Member.GetAsync(
                    c =>
                    {
                        c.QueryParameters.MemberTypeId = memberTypeId;
                        c.QueryParameters.Take = 1;
                    },
                    ct
                );
                return (int)(paged?.Total ?? 0);
            }
        );

    // ── Users ─────────────────────────────────────────────────────────────────

    /// <summary>Maps a generated user model onto the command-facing <see cref="UserResponse"/>.</summary>
    /// <param name="user">The generated user model.</param>
    /// <returns>The mapped user (state enum flattened to its name).</returns>
    private static UserResponse MapUser(Gen.UserResponseModel user) =>
        new()
        {
            Id = user.Id ?? Guid.Empty,
            Email = user.Email ?? "",
            Name = user.Name ?? "",
            UserName = user.UserName ?? "",
            State = user.State?.ToString() ?? "",
            CreateDate = user.CreateDate ?? default,
        };

    /// <summary>Lists users via <c>GET user?skip=&amp;take=</c> (generated client, #79).</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of users mapped to <see cref="UserResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.User.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<UserResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapUser).ToList(),
                };
            }
        );

    /// <summary>Gets a single user by id via <c>GET user/{id}</c> (generated client, #79).</summary>
    /// <param name="id">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user mapped to <see cref="UserResponse"/>.</returns>
    public Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var user = await _api
                    .Umbraco.Management.Api.V1.User[id]
                    .GetAsync(cancellationToken: ct);
                return user is null ? new UserResponse { Id = id } : MapUser(user);
            }
        );

    /// <summary>
    /// Invites a user via <c>POST user/invite</c> (generated client). The endpoint sends the
    /// invitation email and returns no body, so an empty success response is returned.
    /// </summary>
    /// <param name="request">
    /// The invite details. Groups can be given as ids (<see cref="InviteUserRequest.UserGroupIds"/>)
    /// or as alias, name or id references (<see cref="InviteUserRequest.UserGroups"/>); both are sent.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure (a 404 when a group reference matches no group).</returns>
    public Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var groupIds = request
                    .UserGroupIds.Select(g => g.Id)
                    .Concat(await ResolveUserGroupIdsAsync(request.UserGroups, ct));
                var body = new Gen.InviteUserRequestModel
                {
                    Email = request.Email,
                    Name = request.Name,
                    // Umbraco refuses an invite with no userName, and by default one whose
                    // userName differs from the email (#215), so the email is the default.
                    UserName = request.UserName ?? request.Email,
                    Message = request.Message,
                    UserGroupIds = groupIds
                        .Select(id => new Gen.ReferenceByIdModel { Id = id })
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

    /// <summary>Maps a generated dictionary item (from the by-id read) onto the command-facing
    /// <see cref="DictionaryItemResponse"/>, including its translations.</summary>
    /// <param name="item">The generated dictionary item model.</param>
    /// <returns>The mapped dictionary item with translations.</returns>
    private static DictionaryItemResponse MapDictionaryItem(Gen.DictionaryItemResponseModel item) =>
        new()
        {
            Id = item.Id ?? Guid.Empty,
            Name = item.Name ?? "",
            Translations = (item.Translations ?? [])
                .Select(t => new DictionaryTranslation
                {
                    IsoCode = t.IsoCode ?? "",
                    Translation = t.Translation ?? "",
                })
                .ToList(),
        };

    /// <summary>
    /// Lists dictionary items via <c>GET dictionary?skip=&amp;take=</c> (generated client, #79).
    /// The list (overview) response carries only names and iso codes, not translation text, so
    /// list items have no translations - a per-item by-id read is needed for those.
    /// </summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of dictionary items (id + name) mapped to <see cref="DictionaryItemResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Dictionary.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<DictionaryItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DictionaryItemResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                        })
                        .ToList(),
                };
            }
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
        // #211: the shared resolver reads every page (this used to stop at 1,000 items).
        var resolved = await ResolveIdAsync(EntityKind.DictionaryItem, keyOrId, ct);
        if (!resolved.IsSuccess)
            return UmbracoResponse<DictionaryItemResponse>.FailureFrom(resolved);
        var id = resolved.Data;

        return await GuardedApiAsync(
            ct,
            async () =>
            {
                var item = await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .GetAsync(cancellationToken: ct);
                return item is null
                    ? new DictionaryItemResponse { Id = id }
                    : MapDictionaryItem(item);
            }
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
                await GuardDictionaryIsoCodesAsync(request.Translations, ct);

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
                    // #110: create under a parent when one is supplied; a null parent creates at the root.
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                };
                await _api.Umbraco.Management.Api.V1.Dictionary.PostAsync(
                    body,
                    cancellationToken: ct
                );

                // #181: this used to echo request.Translations, so a code Umbraco silently
                // discarded still came back looking saved. Re-read - by the id we generated, not
                // by name, which would mean listing every item and could match a different one if
                // names are not unique - and report what the instance actually kept.
                var stored = await ReadDictionaryItemAsync(id, ct);
                if (stored.IsSuccess && stored.Data is { } item)
                    return item;

                // The write succeeded but the read did not. Say so rather than fabricating the
                // translations, which is the lie this fix exists to remove.
                return new DictionaryItemResponse
                {
                    Id = id,
                    Name = request.Name,
                    Translations = null,
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

    /// <summary>
    /// Maps a generated webhook model onto the command-facing <see cref="WebhookResponse"/>.
    /// Events are objects, not strings (#46). Custom headers land in the generated model's
    /// additional-data bag; they are flattened to a string map best-effort.
    /// </summary>
    /// <param name="webhook">The generated webhook model.</param>
    /// <returns>The mapped webhook.</returns>
    private static WebhookResponse MapWebhook(Gen.WebhookResponseModel webhook) =>
        new()
        {
            Id = webhook.Id ?? Guid.Empty,
            Name = webhook.Name,
            Description = webhook.Description,
            Url = webhook.Url ?? "",
            Enabled = webhook.Enabled ?? false,
            ContentTypeKeys = (webhook.ContentTypeKeys ?? [])
                .Where(k => k is not null)
                .Select(k => k!.Value)
                .ToList(),
            Events = (webhook.Events ?? [])
                .Select(e => new WebhookEvent
                {
                    EventName = e.EventName ?? "",
                    EventType = e.EventType,
                    Alias = e.Alias,
                })
                .ToList(),
            Headers = webhook.Headers?.AdditionalData is { Count: > 0 } headers
                ? headers.ToDictionary(
                    kv => kv.Key,
                    kv =>
                        kv.Value switch
                        {
                            UntypedString s => s.GetValue() ?? "",
                            _ => kv.Value?.ToString() ?? "",
                        }
                )
                : null,
        };

    /// <summary>Lists webhooks via <c>GET webhook?skip=&amp;take=</c> (generated client, #79).</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of webhooks mapped to <see cref="WebhookResponse"/>.</returns>
    public Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Webhook.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<WebhookResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapWebhook).ToList(),
                };
            }
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
            // write tells the user WHICH field failed (#48). The generated ProblemDetails has
            // no typed property for it; it lands in AdditionalData as an UntypedNode.
            var fieldErrors = FormatProblemDetailsErrors(pd);
            var message = fieldErrors is null ? baseMessage : $"{baseMessage} ({fieldErrors})";
            // A declared error body: a 5xx is a server-side problem, anything else the server
            // rejecting the request (#152).
            return UmbracoResponse<T>.Failure(status, message, CategoryFor(status));
        }
        catch (ApiException ex)
        {
            // ResponseStatusCode is 0 when Kiota never got an HTTP response.
            return UmbracoResponse<T>.Failure(
                ex.ResponseStatusCode,
                DescribeApiException(ex),
                CategoryFor(ex.ResponseStatusCode)
            );
        }
        catch (HttpRequestException ex)
        {
            return UmbracoResponse<T>.Failure(
                0,
                $"Could not reach the Umbraco instance: {ex.Message}",
                FailureCategory.Unreachable
            );
        }
        // A timeout surfaces as a cancellation whose token is NOT the caller's; a genuine
        // caller cancellation (ct signalled) is rethrown so callers can observe it.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UmbracoResponse<T>.Failure(
                0,
                "The request to the Umbraco instance timed out.",
                FailureCategory.Timeout
            );
        }
    }

    /// <summary>
    /// Classifies an HTTP status into a <see cref="FailureCategory"/> for a response the server
    /// actually returned (#152): a 5xx (or an undeclared status carried as a 0-less code) is a
    /// server-side error, a 4xx is the request being rejected, and a 0 means no response arrived.
    /// </summary>
    /// <param name="status">The HTTP status code from the response, or 0 when none was received.</param>
    /// <returns>The matching failure category.</returns>
    private static FailureCategory CategoryFor(int status) =>
        status switch
        {
            0 => FailureCategory.Unreachable,
            >= 500 => FailureCategory.ServerError,
            >= 400 => FailureCategory.RequestRejected,
            _ => FailureCategory.ServerError,
        };

    /// <summary>
    /// Builds a 404 <see cref="ApiException"/> for client-side resolution failures (e.g. an
    /// alias/key that matches no resource), so <see cref="GuardedApiAsync{T}"/> maps it to a
    /// normal 404 failure rather than a thrown exception.
    /// </summary>
    /// <param name="message">The not-found message to surface.</param>
    /// <returns>An <see cref="ApiException"/> with status 404.</returns>
    private static ApiException NotFound(string message) =>
        new(message) { ResponseStatusCode = 404 };

    /// <summary>Builds a 400 for a request this client refuses to send.</summary>
    /// <param name="message">What is wrong, and what the caller can do about it.</param>
    /// <returns>An exception <see cref="GuardedApiAsync{T}"/> maps to a rejected request.</returns>
    private static ApiException BadRequest(string message) =>
        new(message) { ResponseStatusCode = 400 };

    /// <summary>The instance's configured language ISO codes, read once per client.</summary>
    private HashSet<string>? _knownIsoCodes;

    /// <summary>
    /// The ISO codes of every language on the instance, cached for the life of the client so a
    /// batch of dictionary creates pays for the lookup once.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The codes, or null when the language list could not be read.</returns>
    private async Task<HashSet<string>?> KnownIsoCodesAsync(CancellationToken ct)
    {
        if (_knownIsoCodes is not null)
            return _knownIsoCodes;

        var languages = await _api.Umbraco.Management.Api.V1.Language.GetAsync(
            c => c.QueryParameters.Take = 1000,
            ct
        );
        var codes = (languages?.Items ?? [])
            .Select(l => l.IsoCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        // Umbraco always has at least one language, so an empty list means the read did not work.
        // Only a real answer is cached.
        if (codes.Count == 0)
            return null;

        _knownIsoCodes = codes;
        return codes;
    }

    /// <summary>
    /// Produces a legible message for a Kiota <see cref="ApiException"/>. When the server returns
    /// a status the OpenAPI spec did not declare, Kiota's own message is the unhelpful "The server
    /// returned an unexpected status code and no error factory is registered for this code: NNN"
    /// (seen on Umbraco 17.x member delete, which 500s server-side - see <see cref="DeleteMemberAsync"/>).
    /// Rewrite that into something a user can act on, distinguishing a server-side 5xx from other
    /// undeclared codes, while keeping any genuinely useful message intact.
    /// </summary>
    /// <param name="ex">The exception thrown by the generated client.</param>
    /// <returns>A human-readable error message.</returns>
    private static string DescribeApiException(ApiException ex)
    {
        var code = ex.ResponseStatusCode;
        var isUndeclared =
            string.IsNullOrWhiteSpace(ex.Message)
            || ex.Message.Contains("no error factory is registered", StringComparison.Ordinal);
        if (!isUndeclared)
            return ex.Message;
        return code >= 500
            ? $"The Umbraco server returned an internal error (HTTP {code}). This is a "
                + "server-side problem, not a rejected request; check the Umbraco logs."
            : $"The Umbraco server returned an unexpected HTTP {code} with no error details.";
    }

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
                ? new ContentTypeRef { Id = dtId }
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
            MediaType = item.MediaType?.Id is { } mtId ? new ContentTypeRef { Id = mtId } : null,
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
            MemberType = item.MemberType?.Id is { } mtId ? new ContentTypeRef { Id = mtId } : null,
            IsApproved = item.IsApproved ?? false,
            IsLockedOut = item.IsLockedOut ?? false,
            CreateDate = variant?.CreateDate ?? default,
            // #185: the read was as narrow as content's was before Phase 3 - groups and property
            // values were being fetched and dropped at this mapping.
            Username = item.Username,
            Groups = (item.Groups ?? [])
                .Where(g => g is not null)
                .Select(g => new MemberGroupRef { Id = g!.Value })
                .ToList(),
            Values = (item.Values ?? [])
                .Select(v => new ContentValueResponse
                {
                    Alias = v.Alias ?? "",
                    Culture = v.Culture,
                    Segment = v.Segment,
                    Value = UntypedNodeFactory.ToJsonNode(v.Value),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Flattens the RFC-9110 field-level <c>errors</c> map from a generated
    /// <see cref="Gen.ProblemDetails"/> into a readable "field: msg; ..." string, so a
    /// write rejected on the Kiota path tells the user which field failed (#48). The generated
    /// ProblemDetails has no typed <c>errors</c> property, so the map arrives under
    /// <see cref="Gen.ProblemDetails.AdditionalData"/> as a Kiota <see cref="UntypedNode"/> tree
    /// (an object of field -> array-of-message-strings).
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
        // commonly, as an array of such objects ([{"field":["msg"]}]) — handle both (#48).
        if (raw is UntypedObject errorsObj)
            CollectFromObject(errorsObj);
        else if (raw is UntypedArray errorsArr)
            foreach (var element in errorsArr.GetValue())
                if (element is UntypedObject elementObj)
                    CollectFromObject(elementObj);

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }
}
