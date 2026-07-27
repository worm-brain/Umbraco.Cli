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

    public async Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(
        CreateContentRequest request,
        CancellationToken ct = default
    ) =>
        await PostAsync<CreateContentRequest, ContentItemResponse>(
            "umbraco/management/api/v1/document",
            request,
            ct
        );

    public async Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        CancellationToken ct = default
    ) =>
        await PutAsync<UpdateContentRequest, ContentItemResponse>(
            $"umbraco/management/api/v1/document/{id}",
            request,
            ct
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

    public async Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    )
    {
        var schedules = (cultures ?? ["*"]).Select(c => new PublishSchedule { Culture = c });
        return await PutAsync<PublishContentRequest, Empty>(
            $"umbraco/management/api/v1/document/{id}/publish",
            new PublishContentRequest { PublishSchedules = schedules },
            ct
        );
    }

    public async Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    )
    {
        var schedules = (cultures ?? ["*"]).Select(c => new PublishSchedule { Culture = c });
        return await PutAsync<PublishContentRequest, Empty>(
            $"umbraco/management/api/v1/document/{id}/unpublish",
            new PublishContentRequest { PublishSchedules = schedules },
            ct
        );
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

    public async Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default
    )
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(parentId.ToString()), "parentId" },
            { new StringContent(name), "name" },
            {
                new StreamContent(fileStream)
                {
                    Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) },
                },
                "file",
                fileName
            },
        };
        return await SendAsync<MediaItemResponse>(
            HttpMethod.Post,
            "umbraco/management/api/v1/media",
            form,
            ct
        );
    }

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

    public async Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken ct = default
    ) =>
        await PostAsync<CreateMemberRequest, MemberResponse>(
            "umbraco/management/api/v1/member",
            request,
            ct
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
                var id = Guid.NewGuid();
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
                var id = Guid.NewGuid();
                var body = new Gen.CreateWebhookRequestModel
                {
                    Id = id,
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
