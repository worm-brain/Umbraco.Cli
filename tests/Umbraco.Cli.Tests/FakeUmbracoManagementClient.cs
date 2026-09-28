using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// In-memory adapter for <see cref="IUmbracoManagementClient"/>. The seam from #26 lets
/// the executor run against canned responses instead of real HTTP. Only the members a
/// test needs are configured; the rest throw to flag unintended calls.
/// </summary>
internal sealed class FakeUmbracoManagementClient : IUmbracoManagementClient
{
    public Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>What <see cref="GetContentAsync"/> lists, whatever the parent (seeded by a test).</summary>
    public List<ContentItemResponse> ContentChildren { get; } = [];

    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<ContentItemResponse>>.Success(
                new PagedResponse<ContentItemResponse>
                {
                    Total = ContentChildren.Count,
                    Items = [.. ContentChildren.Skip(skip).Take(take)],
                }
            )
        );

    // Configurable exemplar used by the executor tests.
    public UmbracoResponse<ContentItemResponse>? ContentByIdResponse { get; set; }
    public Guid? LastRequestedId { get; private set; }

    /// <summary>Documents by id, checked before <see cref="ContentByIdResponse"/>.</summary>
    public Dictionary<Guid, ContentItemResponse> ContentById { get; } = [];

    public Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        LastRequestedId = id;
        if (ContentById.TryGetValue(id, out var byId))
            return Task.FromResult(UmbracoResponse<ContentItemResponse>.Success(byId));
        return Task.FromResult(
            ContentByIdResponse
                ?? throw new InvalidOperationException("ContentByIdResponse not configured.")
        );
    }

    /// <summary>The request of the last <see cref="CreateContentAsync"/> call (#228).</summary>
    public CreateContentRequest? LastCreate { get; private set; }

    /// <summary>Records the request and answers with a bare item carrying its id.</summary>
    /// <param name="request">The create request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success carrying the request's id.</returns>
    public Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(
        CreateContentRequest request,
        CancellationToken ct = default
    )
    {
        LastCreate = request;
        return Task.FromResult(
            UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse { Id = request.Id ?? Guid.NewGuid() }
            )
        );
    }

    /// <summary>The (id, request, replace) of the last <see cref="UpdateContentAsync"/> call (#178/#179).</summary>
    public (Guid Id, UpdateContentRequest Request, bool Replace)? LastUpdate { get; private set; }

    /// <summary>Response returned by <see cref="UpdateContentAsync"/>; a bare success when unset.</summary>
    public UmbracoResponse<ContentItemResponse>? UpdateContentResponse { get; set; }

    public Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        bool replace = false,
        CancellationToken ct = default
    )
    {
        LastUpdate = (id, request, replace);
        return Task.FromResult(
            UpdateContentResponse
                ?? UmbracoResponse<ContentItemResponse>.Success(new ContentItemResponse { Id = id })
        );
    }

    // Optional per-id handlers used by the bulk-operation tests (#85). When null the method
    // throws (flagging an unintended call), matching the fake's default behaviour.
    public Func<Guid, UmbracoResponse<Empty>>? DeleteContentHandler { get; set; }
    public Func<Guid, UmbracoResponse<Empty>>? PublishContentHandler { get; set; }

    /// <summary>Ids passed to <see cref="DeleteContentAsync"/> / <see cref="PublishContentAsync"/>, in order.</summary>
    public List<Guid> CalledIds { get; } = [];

    public Task<UmbracoResponse<DomainsResponse>> GetDomainsAsync(
        Guid id,
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<DomainsResponse>.Success(new DomainsResponse()));

    /// <summary>The last domains written, for #180.</summary>
    public SetDomainsRequest? LastDomains { get; private set; }

    public Task<UmbracoResponse<DomainsResponse>> SetDomainsAsync(
        Guid id,
        SetDomainsRequest request,
        CancellationToken ct = default
    )
    {
        LastDomains = request;
        return Task.FromResult(
            UmbracoResponse<DomainsResponse>.Success(
                new DomainsResponse
                {
                    DefaultIsoCode = request.DefaultIsoCode,
                    Domains = request.Domains,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> DeleteContentAsync(Guid id, CancellationToken ct = default)
    {
        if (DeleteContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        return Task.FromResult(DeleteContentHandler(id));
    }

    /// <summary>The (publishAt, unpublishAt) of the last publish call (#90).</summary>
    public (DateTimeOffset? PublishAt, DateTimeOffset? UnpublishAt)? LastPublishSchedule
    {
        get;
        private set;
    }

    public Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        DateTimeOffset? publishAt = null,
        DateTimeOffset? unpublishAt = null,
        CancellationToken ct = default
    )
    {
        LastPublishSchedule = (publishAt, unpublishAt);
        if (PublishContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        StateCalls.Add(("publish", id, cultures?.ToList()));
        return Task.FromResult(PublishContentHandler(id));
    }

    public Func<Guid, UmbracoResponse<Empty>>? UnpublishContentHandler { get; set; }

    /// <summary>Every publish/unpublish call in order, with the cultures it named (null = all/invariant).</summary>
    public List<(string Operation, Guid Id, List<string>? Cultures)> StateCalls { get; } = [];

    public Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    )
    {
        if (UnpublishContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        StateCalls.Add(("unpublish", id, cultures?.ToList()));
        return Task.FromResult(UnpublishContentHandler(id));
    }

    public Task<UmbracoResponse<Empty>> TrashContentAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The (id, target) of the last <see cref="RestoreContentAsync"/> call (#230).</summary>
    public (Guid Id, RestoreTarget? Target)? LastRestore { get; private set; }

    /// <summary>Records the call and answers with a bare success.</summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="target">The requested target.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A bare success.</returns>
    public Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        RestoreTarget? target = null,
        CancellationToken ct = default
    )
    {
        LastRestore = (id, target);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> EmptyContentRecycleBinAsync(
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> MoveContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The (id, parentId) of the last copy call (#91).</summary>
    public (Guid Id, Guid? ParentId)? LastCopyArgs { get; private set; }

    /// <summary>The response the fake returns for a copy; a default carrying a new id is used when unset (#91).</summary>
    public UmbracoResponse<ContentItemResponse>? CopyContentResponse { get; set; }

    public Task<UmbracoResponse<ContentItemResponse>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    )
    {
        LastCopyArgs = (id, parentId);
        return Task.FromResult(
            CopyContentResponse
                ?? UmbracoResponse<ContentItemResponse>.Success(
                    new ContentItemResponse { Id = Guid.NewGuid() }
                )
        );
    }

    /// <summary>Recorded content sort calls, in <c>(parentId, orderedChildIds)</c> order (#88).</summary>
    public List<(Guid? ParentId, IReadOnlyList<Guid> OrderedChildIds)> ContentSorted { get; } = [];

    public Task<UmbracoResponse<Empty>> SortContentAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    )
    {
        ContentSorted.Add((parentId, orderedChildIds));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>Content tree items <see cref="GetContentTreeAsync"/> returns (seeded by a test) (#89).</summary>
    public List<TreeItem> ContentTreeNodes { get; } = [];

    /// <summary>The (parentId, maxDepth) of the last content tree call (#89).</summary>
    public (Guid? ParentId, int MaxDepth)? LastContentTreeArgs { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetContentTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    )
    {
        LastContentTreeArgs = (parentId, maxDepth);
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<TreeItem>>.Success(ContentTreeNodes.ToList())
        );
    }

    /// <summary>Content items <see cref="FindContentByNameAsync"/> returns (seeded by a test) (#89).</summary>
    public List<ContentItemResponse> ContentFindResults { get; } = [];

    /// <summary>The (query, parentId) of the last content find-by-name call (#89).</summary>
    public (string Query, Guid? ParentId)? LastContentFindByName { get; private set; }

    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> FindContentByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    )
    {
        LastContentFindByName = (query, parentId);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<ContentItemResponse>>.Success(
                new PagedResponse<ContentItemResponse>
                {
                    Total = ContentFindResults.Count,
                    Items = ContentFindResults.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    /// <summary>Content items <see cref="FindContentByPathAsync"/> returns (seeded by a test) (#89).</summary>
    public List<ContentItemResponse> ContentFindPathResults { get; } = [];

    /// <summary>The path of the last content find-by-path call (#89).</summary>
    public string? LastContentFindByPath { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<ContentItemResponse>>> FindContentByPathAsync(
        string path,
        CancellationToken ct = default
    )
    {
        LastContentFindByPath = path;
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<ContentItemResponse>>.Success(
                ContentFindPathResults.ToList()
            )
        );
    }

    /// <summary>The (id, wait) of the last publish-descendants call (#90).</summary>
    public (Guid Id, bool Wait)? LastPublishDescendantsArgs { get; private set; }

    /// <summary>The response the fake returns for publish-descendants; a default is used when unset (#90).</summary>
    public UmbracoResponse<PublishDescendantsResult>? PublishDescendantsResponse { get; set; }

    public Task<UmbracoResponse<PublishDescendantsResult>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        bool wait = false,
        CancellationToken ct = default
    )
    {
        LastPublishDescendantsArgs = (id, wait);
        return Task.FromResult(
            PublishDescendantsResponse
                ?? UmbracoResponse<PublishDescendantsResult>.Success(
                    new PublishDescendantsResult { TaskId = Guid.NewGuid(), IsComplete = wait }
                )
        );
    }

    public Task<UmbracoResponse<PagedResponse<DocumentVersionResponse>>> GetDocumentVersionsAsync(
        Guid documentId,
        string? culture = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>Body returned by <see cref="GetDocumentVersionAsync"/>; the version ids read are in <see cref="CalledIds"/>.</summary>
    public JsonNode? DocumentVersionBody { get; set; }

    /// <summary>Returns <see cref="DocumentVersionBody"/>, or a 404 when unset.</summary>
    /// <param name="versionId">The version id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The configured body, or a 404.</returns>
    public Task<UmbracoResponse<JsonNode>> GetDocumentVersionAsync(
        Guid versionId,
        CancellationToken ct = default
    )
    {
        CalledIds.Add(versionId);
        return Task.FromResult(
            DocumentVersionBody is { } body
                ? UmbracoResponse<JsonNode>.Success(body)
                : UmbracoResponse<JsonNode>.Failure(404, "Version not found.")
        );
    }

    /// <summary>The document <see cref="GetVersionDocumentIdAsync"/> answers with; null answers 404.</summary>
    public Guid? VersionDocumentId { get; set; }

    /// <summary>Returns <see cref="VersionDocumentId"/>, or a 404 when unset.</summary>
    /// <param name="versionId">The version id (ignored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The configured document id, or a 404.</returns>
    public Task<UmbracoResponse<Guid>> GetVersionDocumentIdAsync(
        Guid versionId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            VersionDocumentId is { } id
                ? UmbracoResponse<Guid>.Success(id)
                : UmbracoResponse<Guid>.Failure(404, "Version not found.")
        );

    /// <summary>The (version id, culture) of the last <see cref="RollbackDocumentVersionAsync"/> call (#233).</summary>
    public (Guid VersionId, string? Culture)? LastRollback { get; private set; }

    /// <summary>Records the call and answers with a bare success.</summary>
    /// <param name="versionId">The version rolled back to.</param>
    /// <param name="culture">The culture rolled back.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A bare success.</returns>
    public Task<UmbracoResponse<Empty>> RollbackDocumentVersionAsync(
        Guid versionId,
        string? culture = null,
        CancellationToken ct = default
    )
    {
        LastRollback = (versionId, culture);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The (id, values) of the last upload (#226, #220).</summary>
    public (Guid? Id, IReadOnlyList<MediaValue>? Values)? LastUpload { get; private set; }

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
    )
    {
        LastUpload = (id, values);
        return Task.FromResult(
            UmbracoResponse<MediaItemResponse>.Success(
                new MediaItemResponse { Id = id ?? Guid.NewGuid(), Name = name }
            )
        );
    }

    public Task<UmbracoResponse<MediaItemResponse>> CreateMediaFolderAsync(
        string name,
        Guid? parentId = null,
        Guid? id = null,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<MediaItemResponse>.Success(
                new MediaItemResponse { Id = id ?? Guid.NewGuid(), Name = name }
            )
        );

    public Task<UmbracoResponse<Empty>> DeleteMediaAsync(Guid id, CancellationToken ct = default) =>
        throw new NotImplementedException();

    /// <summary>Ids passed to <see cref="TrashMediaAsync"/>, in call order (#226).</summary>
    public List<Guid> MediaTrashed { get; } = [];

    public Task<UmbracoResponse<Empty>> TrashMediaAsync(Guid id, CancellationToken ct = default)
    {
        MediaTrashed.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // ── Media snapshot (IMediaSnapshotClient, #226) ────────────────────────────

    /// <summary>The placements <see cref="GetMediaSnapshotTreeAsync"/> returns, in order.</summary>
    public List<ContentTreeNode> MediaSnapshotTree { get; } = [];

    /// <summary>Canned raw media bodies, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> MediaRaw { get; } = [];

    /// <summary>The bytes <see cref="DownloadMediaFileAsync"/> serves, keyed by <c>src</c>; a missing one is a 404.</summary>
    public Dictionary<string, byte[]> MediaFiles { get; } = [];

    /// <summary>Every file staged, in order: its name, content and the id it was given.</summary>
    public List<(string FileName, byte[] Content, Guid Id)> StagedFiles { get; } = [];

    /// <summary>Downloads that fail with the given status, keyed by <c>src</c> (a missing <see cref="MediaFiles"/> entry is a 404).</summary>
    public Dictionary<string, int> MediaFileErrors { get; } = [];

    /// <summary>How many downloads were made.</summary>
    public int MediaDownloads { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetMediaSnapshotTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IReadOnlyList<ContentTreeNode>>.Success(MediaSnapshotTree.ToList())
        );

    public Task<UmbracoResponse<JsonNode>> GetMediaRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => Raw(MediaRaw, id);

    /// <summary>Records the create in <see cref="RawWrites"/> as kind <c>media</c>.</summary>
    public Task<UmbracoResponse<Empty>> CreateMediaRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("media", null, body);

    /// <summary>Records the update in <see cref="RawWrites"/> as kind <c>media</c>.</summary>
    public Task<UmbracoResponse<Empty>> UpdateMediaRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("media", id, body);

    public async Task<UmbracoResponse<Guid>> StageTemporaryFileAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct = default
    )
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        var id = Guid.NewGuid();
        StagedFiles.Add((fileName, copy.ToArray(), id));
        return UmbracoResponse<Guid>.Success(id);
    }

    public async Task<UmbracoResponse<Empty>> DownloadMediaFileAsync(
        string src,
        Stream destination,
        CancellationToken ct = default
    )
    {
        MediaDownloads++;
        if (MediaFileErrors.TryGetValue(src, out var status))
            return UmbracoResponse<Empty>.Failure(status, $"Failed: {src}");
        if (!MediaFiles.TryGetValue(src, out var bytes))
            return UmbracoResponse<Empty>.Failure(404, $"Not found: {src}");
        await destination.WriteAsync(bytes, ct);
        return UmbracoResponse<Empty>.Success(Empty.Value);
    }

    /// <summary>The (id, target) of the last media restore.</summary>
    public (Guid Id, RestoreTarget? Target)? LastMediaRestore { get; private set; }

    public Task<UmbracoResponse<Empty>> RestoreMediaAsync(
        Guid id,
        RestoreTarget? target = null,
        CancellationToken ct = default
    )
    {
        LastMediaRestore = (id, target);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> EmptyMediaRecycleBinAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> MoveMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>Recorded media sort calls, in <c>(parentId, orderedChildIds)</c> order (#88).</summary>
    public List<(Guid? ParentId, IReadOnlyList<Guid> OrderedChildIds)> MediaSorted { get; } = [];

    public Task<UmbracoResponse<Empty>> SortMediaAsync(
        Guid? parentId,
        IReadOnlyList<Guid> orderedChildIds,
        CancellationToken ct = default
    )
    {
        MediaSorted.Add((parentId, orderedChildIds));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>Media tree items <see cref="GetMediaTreeAsync"/> returns (seeded by a test) (#89).</summary>
    public List<TreeItem> MediaTreeNodes { get; } = [];

    /// <summary>The (parentId, maxDepth) of the last media tree call (#89).</summary>
    public (Guid? ParentId, int MaxDepth)? LastMediaTreeArgs { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> GetMediaTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    )
    {
        LastMediaTreeArgs = (parentId, maxDepth);
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<TreeItem>>.Success(MediaTreeNodes.ToList())
        );
    }

    /// <summary>Media items <see cref="FindMediaByNameAsync"/> returns (seeded by a test) (#89).</summary>
    public List<MediaItemResponse> MediaFindResults { get; } = [];

    /// <summary>The (query, parentId) of the last media find-by-name call (#89).</summary>
    public (string Query, Guid? ParentId)? LastMediaFindByName { get; private set; }

    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> FindMediaByNameAsync(
        string query,
        Guid? parentId,
        int skip,
        int take,
        CancellationToken ct = default
    )
    {
        LastMediaFindByName = (query, parentId);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<MediaItemResponse>>.Success(
                new PagedResponse<MediaItemResponse>
                {
                    Total = MediaFindResults.Count,
                    Items = MediaFindResults.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    /// <summary>Media items <see cref="FindMediaByPathAsync"/> returns (seeded by a test) (#89).</summary>
    public List<MediaItemResponse> MediaFindPathResults { get; } = [];

    /// <summary>The path of the last media find-by-path call (#89).</summary>
    public string? LastMediaFindByPath { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<MediaItemResponse>>> FindMediaByPathAsync(
        string path,
        CancellationToken ct = default
    )
    {
        LastMediaFindByPath = path;
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<MediaItemResponse>>.Success(MediaFindPathResults.ToList())
        );
    }

    public Task<UmbracoResponse<PagedResponse<MediaTypeResponse>>> GetMediaTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Guid>> CreateMediaTypeAsync(
        CreateMediaTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteMediaTypeAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // ── Schema list + delete (configurable for the export/diff/apply tests, #68) ──
    // The three schema list methods page over these backing lists (one page returns every
    // configured item); tests populate them to drive SchemaExporter. Left empty by default so
    // an export over an unconfigured fake sees no entities rather than throwing.

    /// <summary>Document-type tree items the paged list returns (id + name feed enumeration).</summary>
    public List<DocumentTypeResponse> DocumentTypeList { get; } = [];

    /// <summary>Data-type tree items the paged list returns.</summary>
    public List<DataTypeResponse> DataTypeList { get; } = [];

    /// <summary>Template tree items the paged list returns.</summary>
    public List<TemplateResponse> TemplateList { get; } = [];

    /// <summary>Ids passed to any schema <c>Delete*Async</c>, in call order.</summary>
    public List<Guid> SchemaDeletedIds { get; } = [];

    /// <summary>Returns one page of <paramref name="source"/> honouring skip/take, as a success.</summary>
    private static Task<UmbracoResponse<PagedResponse<T>>> Page<T>(
        IReadOnlyList<T> source,
        int skip,
        int take
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<T>>.Success(
                new PagedResponse<T>
                {
                    Total = source.Count,
                    Items = source.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => Page(DocumentTypeList, skip, take);

    /// <summary>The alias or id passed to <see cref="GetDocumentTypeAsync"/>, for #159.</summary>
    public string? LastDocumentTypeLookup { get; private set; }

    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeAsync(
        string aliasOrId,
        CancellationToken ct = default
    )
    {
        LastDocumentTypeLookup = aliasOrId;
        return Task.FromResult(
            UmbracoResponse<DocumentTypeResponse>.Success(
                new DocumentTypeResponse { Name = "Blog Post", Alias = "blogPost" }
            )
        );
    }

    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Guid>> CreateDocumentTypeAsync(
        CreateDocumentTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteDocumentTypeAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0,
        int take = 20,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => Page(DataTypeList, skip, take);

    public Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>Every flag-built data-type create, in call order (#285 read-back tests).</summary>
    public List<CreateDataTypeRequest> DataTypeCreates { get; } = [];

    /// <summary>When set, a flag-built data-type create returns this failure instead.</summary>
    public UmbracoResponse<Guid>? DataTypeCreateFailure { get; set; }

    /// <summary>
    /// Records the create and echoes the request, as the real flag-built create does; the command
    /// reads the saved item back through <see cref="GetSchemaRawAsync"/>.
    /// </summary>
    /// <param name="request">The create request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id, or <see cref="DataTypeCreateFailure"/>.</returns>
    public Task<UmbracoResponse<Guid>> CreateDataTypeAsync(
        CreateDataTypeRequest request,
        CancellationToken ct = default
    )
    {
        DataTypeCreates.Add(request);
        return Task.FromResult(
            DataTypeCreateFailure ?? UmbracoResponse<Guid>.Success(request.Id ?? Guid.Empty)
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        Guid id,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        string nameOrId,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteDataTypeAsync(Guid id, CancellationToken ct = default)
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>The languages <see cref="GetLanguagesAsync"/> returns (the content diff reads the default, #293).</summary>
    public List<LanguageResponse> LanguageList { get; } = [];

    public Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IEnumerable<LanguageResponse>>.Success(LanguageList.ToList())
        );

    /// <summary>How many times <see cref="GetDocumentTypeAliasesAsync"/> was called (#293).</summary>
    public int DocumentTypeAliasLookups { get; private set; }

    public Task<UmbracoResponse<IReadOnlyDictionary<Guid, string>>> GetDocumentTypeAliasesAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default
    )
    {
        DocumentTypeAliasLookups++;
        var wanted = ids.ToHashSet();
        IReadOnlyDictionary<Guid, string> aliases = DocumentTypeList
            .Where(t => wanted.Contains(t.Id))
            .ToDictionary(t => t.Id, t => t.Alias);
        return Task.FromResult(UmbracoResponse<IReadOnlyDictionary<Guid, string>>.Success(aliases));
    }

    public Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(
        CreateLanguageRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<LanguageResponse>> UpdateLanguageAsync(
        string isoCode,
        UpdateLanguageRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>ISO codes passed to <see cref="DeleteLanguageAsync"/>, in call order (#227).</summary>
    public List<string> LanguagesDeleted { get; } = [];

    public Task<UmbracoResponse<Empty>> DeleteLanguageAsync(
        string isoCode,
        CancellationToken ct = default
    )
    {
        LanguagesDeleted.Add(isoCode);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => Page(TemplateList, skip, take);

    public Task<UmbracoResponse<TemplateResponse>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> UpdateTemplateAsync(
        Guid id,
        UpdateTemplateRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The document types using each template, for <see cref="GetTemplateUsageAsync"/> (#269).</summary>
    public Dictionary<Guid, List<TemplateUser>> TemplateUsers { get; } = [];

    /// <summary>How many times <see cref="GetTemplateUsageAsync"/> was called.</summary>
    public int TemplateUsageReads { get; private set; }

    /// <summary>Returns <see cref="TemplateUsers"/>.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The usage map.</returns>
    public Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>
    > GetTemplateUsageAsync(CancellationToken ct = default)
    {
        TemplateUsageReads++;
        return Task.FromResult(
            UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>.Success(
                TemplateUsers.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<TemplateUser>)kv.Value
                )
            )
        );
    }

    /// <summary>Member counts by member group id, for <see cref="CountMembersInGroupAsync"/> (#269).</summary>
    public Dictionary<Guid, int> MemberCountsByGroup { get; } = [];

    /// <summary>Returns <see cref="MemberCountsByGroup"/> for the group (0 when unset).</summary>
    /// <param name="memberGroupId">The member group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count.</returns>
    public Task<UmbracoResponse<int>> CountMembersInGroupAsync(
        Guid memberGroupId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<int>.Success(MemberCountsByGroup.GetValueOrDefault(memberGroupId))
        );

    /// <summary>User counts by user group id, for <see cref="CountUsersInGroupAsync"/> (#269).</summary>
    public Dictionary<Guid, int> UserCountsByGroup { get; } = [];

    /// <summary>Returns <see cref="UserCountsByGroup"/> for the group (0 when unset).</summary>
    /// <param name="userGroupId">The user group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count.</returns>
    public Task<UmbracoResponse<int>> CountUsersInGroupAsync(
        Guid userGroupId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<int>.Success(UserCountsByGroup.GetValueOrDefault(userGroupId))
        );

    public Task<UmbracoResponse<Empty>> DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The (id, request) of the last member update.</summary>
    public (Guid Id, UpdateMemberRequest Request)? LastMemberUpdate { get; private set; }

    public Task<UmbracoResponse<MemberResponse>> UpdateMemberAsync(
        Guid id,
        UpdateMemberRequest request,
        CancellationToken ct = default
    )
    {
        LastMemberUpdate = (id, request);
        return Task.FromResult(
            UmbracoResponse<MemberResponse>.Success(new MemberResponse { Id = id })
        );
    }

    public Task<UmbracoResponse<Empty>> DeleteMemberAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<MemberTypeResponse>>> GetMemberTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Guid>> CreateMemberTypeAsync(
        CreateMemberTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The (id, request) of the last <see cref="UpdateMemberTypeAsync"/> call.</summary>
    public (Guid Id, UpdateMemberTypeRequest Request)? LastMemberTypeUpdate { get; private set; }

    /// <summary>Records the scalar update and answers with a bare success.</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="request">The scalar changes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A bare success.</returns>
    public Task<UmbracoResponse<Empty>> UpdateMemberTypeAsync(
        Guid id,
        UpdateMemberTypeRequest request,
        CancellationToken ct = default
    )
    {
        LastMemberTypeUpdate = (id, request);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteMemberTypeAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>How many members each member type has; absent means none (#253).</summary>
    public Dictionary<Guid, int> MemberCountsByType { get; } = [];

    public Task<UmbracoResponse<int>> CountMembersOfTypeAsync(
        Guid memberTypeId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<int>.Success(MemberCountsByType.GetValueOrDefault(memberTypeId))
        );

    /// <summary>
    /// What uses each document or media type (#287); absent means nothing (no items, no
    /// compositions, not an element type).
    /// </summary>
    public Dictionary<Guid, TypeUsage> TypeUsages { get; } = [];

    public Task<UmbracoResponse<TypeUsage>> GetDocumentTypeUsageAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<TypeUsage>.Success(TypeUsages.GetValueOrDefault(id) ?? new(0, []))
        );

    public Task<UmbracoResponse<TypeUsage>> GetMediaTypeUsageAsync(
        Guid id,
        CancellationToken ct = default
    ) => GetDocumentTypeUsageAsync(id, ct);

    /// <summary>The users <see cref="GetUsersAsync"/> pages through.</summary>
    public List<UserResponse> UserList { get; } = [];

    public Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<UserResponse>>.Success(
                new PagedResponse<UserResponse>
                {
                    Total = UserList.Count,
                    Items = UserList.Skip(skip).Take(take).ToList(),
                }
            )
        );

    /// <summary>Returns the user with that id from <see cref="UserList"/>, or a 404.</summary>
    public Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UserList.FirstOrDefault(u => u.Id == id) is { } user
                ? UmbracoResponse<UserResponse>.Success(user)
                : UmbracoResponse<UserResponse>.Failure(404, $"No user found with id '{id}'.")
        );

    /// <summary>Recorded user creates.</summary>
    public List<CreateUserRequest> UsersCreated { get; } = [];

    /// <summary>When set, <see cref="CreateUserAsync"/> fails with this message instead.</summary>
    public string? CreateUserFailure { get; set; }

    /// <summary>
    /// Records the create and adds the user to <see cref="UserList"/>, so the command's read-back
    /// finds it; fails instead when <see cref="CreateUserFailure"/> is set.
    /// </summary>
    public Task<UmbracoResponse<Guid>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct = default
    )
    {
        UsersCreated.Add(request);
        if (CreateUserFailure is { } failure)
            return Task.FromResult(UmbracoResponse<Guid>.Failure(400, failure));
        var id = request.Id ?? Guid.NewGuid();
        UserList.Add(
            new UserResponse
            {
                Id = id,
                Email = request.Email,
                Name = request.Name,
                UserName = request.UserName ?? request.Email,
            }
        );
        return Task.FromResult(UmbracoResponse<Guid>.Success(id));
    }

    /// <summary>Recorded user updates, in <c>(id, request)</c> order.</summary>
    public List<(Guid Id, UpdateUserRequest Request)> UsersUpdated { get; } = [];

    /// <summary>Records the update and answers with a bare success.</summary>
    public Task<UmbracoResponse<Empty>> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken ct = default
    )
    {
        UsersUpdated.Add((id, request));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>Recorded single user deletes.</summary>
    public List<Guid> UsersDeleted { get; } = [];

    /// <summary>Recorded bulk user deletes (each call's id list).</summary>
    public List<IReadOnlyList<Guid>> UsersBulkDeleted { get; } = [];

    /// <summary>Records the delete and answers with a bare success.</summary>
    public Task<UmbracoResponse<Empty>> DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        UsersDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>Records the bulk delete and answers with a bare success.</summary>
    public Task<UmbracoResponse<Empty>> DeleteUsersAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    )
    {
        UsersBulkDeleted.Add(ids);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>The last invite request sent, or null if none was.</summary>
    public InviteUserRequest? LastInvite { get; private set; }

    public Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    )
    {
        LastInvite = request;
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(
        string key,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>Recorded dictionary creates (#110 asserts the mapped parent).</summary>
    public List<CreateDictionaryItemRequest> DictionaryItemsCreated { get; } = [];

    public Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(
        CreateDictionaryItemRequest request,
        CancellationToken ct = default
    )
    {
        DictionaryItemsCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<DictionaryItemResponse>.Success(
                new DictionaryItemResponse
                {
                    Id = request.Id ?? Guid.NewGuid(),
                    Name = request.Name,
                    Translations = request.Translations.ToList(),
                }
            )
        );
    }

    /// <summary>Dictionary tree items the tree method returns (seeded by a test) (#110).</summary>
    public List<DictionaryTreeItem> DictionaryTreeItems { get; } = [];

    /// <summary>The (parentId, skip, take) of the last dictionary tree call (#110).</summary>
    public (Guid? ParentId, int Skip, int Take)? LastDictionaryTreeArgs { get; private set; }

    /// <summary>Recorded dictionary moves, in <c>(id, target)</c> order (#110).</summary>
    public List<(Guid Id, Guid? Target)> DictionaryItemsMoved { get; } = [];

    public Task<UmbracoResponse<PagedResponse<DictionaryTreeItem>>> GetDictionaryTreeAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastDictionaryTreeArgs = (parentId, skip, take);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<DictionaryTreeItem>>.Success(
                new PagedResponse<DictionaryTreeItem>
                {
                    Total = DictionaryTreeItems.Count,
                    Items = DictionaryTreeItems.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    /// <summary>The subtree <see cref="WalkDictionaryTreeAsync"/> returns.</summary>
    public List<TreeItem> DictionaryWalk { get; } = [];

    /// <summary>The (parent, maxDepth) of the last dictionary walk.</summary>
    public (Guid? Parent, int MaxDepth)? LastDictionaryWalk { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<TreeItem>>> WalkDictionaryTreeAsync(
        Guid? parentId,
        int maxDepth,
        CancellationToken ct = default
    )
    {
        LastDictionaryWalk = (parentId, maxDepth);
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<TreeItem>>.Success(DictionaryWalk.ToList())
        );
    }

    public Task<UmbracoResponse<DictionaryItemResponse>> UpdateDictionaryItemAsync(
        Guid id,
        UpdateDictionaryItemRequest request,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<DictionaryItemResponse>.Success(
                new DictionaryItemResponse { Id = id, Name = request.Name ?? "" }
            )
        );

    public Task<UmbracoResponse<Empty>> MoveDictionaryItemAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    )
    {
        DictionaryItemsMoved.Add((id, targetId));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>Ids passed to <see cref="DeleteDictionaryItemAsync"/>, in call order (#227).</summary>
    public List<Guid> DictionaryItemsDeleted { get; } = [];

    public Task<UmbracoResponse<Empty>> DeleteDictionaryItemAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        DictionaryItemsDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteWebhookAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    /// <summary>The events <see cref="GetWebhookEventsAsync"/> returns.</summary>
    public List<WebhookEvent> WebhookEvents { get; } = [];

    /// <summary>Returns <see cref="WebhookEvents"/> as one page.</summary>
    /// <param name="skip">Ignored.</param>
    /// <param name="take">Ignored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The canned events.</returns>
    public Task<UmbracoResponse<PagedResponse<WebhookEvent>>> GetWebhookEventsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<WebhookEvent>>.Success(
                new PagedResponse<WebhookEvent>
                {
                    Total = WebhookEvents.Count,
                    Items = WebhookEvents.ToList(),
                }
            )
        );

    // ── Schema raw-JSON access (ISchemaClient, #68) ────────────────────────────
    // Backing stores let export tests hand out canned bodies (keyed by id) and apply tests
    // record every write. A recorded write keeps the entity kind, the target id (null for a
    // create), and the exact body sent, so a test can assert both the plan and the payload.

    /// <summary>Canned raw bodies returned by <see cref="GetSchemaRawAsync"/> for document types, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> DocumentTypeRaw { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetSchemaRawAsync"/> for data types, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> DataTypeRaw { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetSchemaRawAsync"/> for templates, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> TemplateRaw { get; } = [];

    /// <summary>
    /// The ids <see cref="GetMediaTypeIdsAsync"/> enumerates (#186). Deliberately separate from
    /// <see cref="MediaTypeRaw"/> so a test can enumerate an id whose body is missing - the
    /// fail-fast case, which a keys-driven enumeration could never produce.
    /// </summary>
    public List<Guid> MediaTypeIds { get; } = [];

    /// <summary>The ids <see cref="GetMemberTypeIdsAsync"/> enumerates (#186).</summary>
    public List<Guid> MemberTypeIds { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetSchemaRawAsync"/> for media types, keyed by id (#186).</summary>
    public Dictionary<Guid, JsonNode> MediaTypeRaw { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetSchemaRawAsync"/> for member types, keyed by id (#186).</summary>
    public Dictionary<Guid, JsonNode> MemberTypeRaw { get; } = [];

    /// <summary>One recorded raw write: entity kind, target id (null = create), and body sent.</summary>
    public sealed record RawWrite(string Kind, Guid? Id, JsonNode Body);

    /// <summary>Every raw create/update the applier performed, in call order.</summary>
    public List<RawWrite> RawWrites { get; } = [];

    /// <summary>When set, a raw create/update returns this failure instead of success (error-path tests).</summary>
    public UmbracoResponse<Empty>? RawWriteFailure { get; set; }

    // Enumeration returns the ids of the configured tree lists (the production client walks the
    // real tree, skipping folders; the fake's lists already hold only real entities).
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDocumentTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IReadOnlyList<Guid>>.Success(
                DocumentTypeList.Select(d => d.Id).ToList()
            )
        );

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDataTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IReadOnlyList<Guid>>.Success(DataTypeList.Select(d => d.Id).ToList())
        );

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetTemplateIdsAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IReadOnlyList<Guid>>.Success(TemplateList.Select(t => t.Id).ToList())
        );

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMediaTypeIdsAsync(
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IReadOnlyList<Guid>>.Success(MediaTypeIds.ToList()));

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMemberTypeIdsAsync(
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IReadOnlyList<Guid>>.Success(MemberTypeIds.ToList()));

    /// <summary>The canned store and recorded-write tag for a schema kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The store and the tag <see cref="RawWrites"/> uses.</returns>
    private (Dictionary<Guid, JsonNode> Store, string Tag) SchemaStore(EntityKind kind) =>
        kind switch
        {
            EntityKind.DocumentType => (DocumentTypeRaw, "documentType"),
            EntityKind.DataType => (DataTypeRaw, "dataType"),
            EntityKind.Template => (TemplateRaw, "template"),
            EntityKind.MediaType => (MediaTypeRaw, "mediaType"),
            EntityKind.MemberType => (MemberTypeRaw, "memberType"),
            EntityKind.DictionaryItem => (DictionaryItemRaw, "dictionaryItem"),
            EntityKind.MemberGroup => (MemberGroupRaw, "memberGroup"),
            EntityKind.UserGroup => (UserGroupRaw, "userGroup"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    // ── Schema snapshot breadth (#227) ─────────────────────────────────────────

    /// <summary>The bodies <see cref="GetLanguagesRawAsync"/> returns, in order (#227).</summary>
    public List<JsonNode> LanguagesRaw { get; } = [];

    /// <summary>The entries <see cref="GetDictionaryEntriesAsync"/> returns (#227).</summary>
    public List<DictionaryEntry> DictionaryEntries { get; } = [];

    /// <summary>When set, <see cref="GetDictionaryEntriesAsync"/> returns this failure instead.</summary>
    public UmbracoResponse<IReadOnlyList<DictionaryEntry>>? DictionaryEntriesFailure { get; set; }

    /// <summary>The ids <see cref="GetMemberGroupIdsAsync"/> enumerates (#227).</summary>
    public List<Guid> MemberGroupIds { get; } = [];

    /// <summary>The ids <see cref="GetUserGroupIdsAsync"/> enumerates (#227).</summary>
    public List<Guid> UserGroupIds { get; } = [];

    /// <summary>Canned raw dictionary item bodies, keyed by id (#227).</summary>
    public Dictionary<Guid, JsonNode> DictionaryItemRaw { get; } = [];

    /// <summary>Canned raw member group bodies, keyed by id (#227).</summary>
    public Dictionary<Guid, JsonNode> MemberGroupRaw { get; } = [];

    /// <summary>Canned raw user group bodies, keyed by id (#227).</summary>
    public Dictionary<Guid, JsonNode> UserGroupRaw { get; } = [];

    /// <summary>The (isoCode, body) of every <see cref="UpdateLanguageRawAsync"/> call, in order.</summary>
    public List<(string IsoCode, JsonNode Body)> LanguageUpdates { get; } = [];

    public Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetLanguagesRawAsync(
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IReadOnlyList<JsonNode>>.Success(LanguagesRaw.ToList()));

    /// <summary>Records the create in <see cref="RawWrites"/> as kind <c>language</c>.</summary>
    public Task<UmbracoResponse<Empty>> CreateLanguageRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("language", null, body);

    /// <summary>Records the update in <see cref="LanguageUpdates"/> and <see cref="RawWrites"/>.</summary>
    public Task<UmbracoResponse<Empty>> UpdateLanguageRawAsync(
        string isoCode,
        JsonNode body,
        CancellationToken ct = default
    )
    {
        LanguageUpdates.Add((isoCode, body));
        return RecordWrite("language", null, body);
    }

    public Task<UmbracoResponse<IReadOnlyList<DictionaryEntry>>> GetDictionaryEntriesAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            DictionaryEntriesFailure
                ?? UmbracoResponse<IReadOnlyList<DictionaryEntry>>.Success(
                    DictionaryEntries.ToList()
                )
        );

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMemberGroupIdsAsync(
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IReadOnlyList<Guid>>.Success(MemberGroupIds.ToList()));

    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetUserGroupIdsAsync(
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IReadOnlyList<Guid>>.Success(UserGroupIds.ToList()));

    /// <summary>Returns the canned body for the kind and id, or a 404.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="id">The id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The body, or a 404.</returns>
    public Task<UmbracoResponse<JsonNode>> GetSchemaRawAsync(
        EntityKind kind,
        Guid id,
        CancellationToken ct = default
    ) => Raw(SchemaStore(kind).Store, id);

    /// <summary>Records the create in <see cref="RawWrites"/> (id null).</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="body">The body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success, or <see cref="RawWriteFailure"/>.</returns>
    public Task<UmbracoResponse<Empty>> CreateSchemaRawAsync(
        EntityKind kind,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite(SchemaStore(kind).Tag, null, body);

    private static Task<UmbracoResponse<JsonNode>> Raw(Dictionary<Guid, JsonNode> store, Guid id) =>
        Task.FromResult(
            store.TryGetValue(id, out var node)
                ? UmbracoResponse<JsonNode>.Success(node)
                : UmbracoResponse<JsonNode>.Failure(404, $"Not found: {id}")
        );

    private Task<UmbracoResponse<Empty>> RecordWrite(string kind, Guid? id, JsonNode body)
    {
        RawWrites.Add(new RawWrite(kind, id, body));
        return Task.FromResult(RawWriteFailure ?? UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>The (id, request, replace) of the last <see cref="UpdateMediaAsync"/> call (#220).</summary>
    public (Guid Id, UpdateMediaRequest Request, bool Replace)? LastMediaUpdate
    {
        get;
        private set;
    }

    /// <summary>Records the update and answers with a bare item.</summary>
    /// <param name="id">The media id.</param>
    /// <param name="request">The update.</param>
    /// <param name="replace">Whether it replaces.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success carrying the id.</returns>
    public Task<UmbracoResponse<MediaItemResponse>> UpdateMediaAsync(
        Guid id,
        UpdateMediaRequest request,
        bool replace = false,
        CancellationToken ct = default
    )
    {
        LastMediaUpdate = (id, request, replace);
        return Task.FromResult(
            UmbracoResponse<MediaItemResponse>.Success(new MediaItemResponse { Id = id })
        );
    }

    /// <summary>The (kind, id, body, replace) of the last <see cref="MergeSchemaItemAsync"/> call (#201).</summary>
    public (EntityKind Kind, Guid Id, JsonNode Body, bool Replace)? LastSchemaMerge
    {
        get;
        private set;
    }

    /// <summary>Records the merge and answers with a bare success.</summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="id">The item id.</param>
    /// <param name="body">The body.</param>
    /// <param name="replace">Whether the body replaces the item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A bare success.</returns>
    public Task<UmbracoResponse<Empty>> MergeSchemaItemAsync(
        EntityKind kind,
        Guid id,
        JsonNode body,
        bool replace = false,
        CancellationToken ct = default
    )
    {
        // Recorded both ways: the round-trip tests look at the merge call, the apply tests at
        // the write it amounts to (an apply update is a replace).
        LastSchemaMerge = (kind, id, body, replace);
        // Leave the merged item readable, as the server does, so a read-back after it works.
        var store = SchemaStore(kind).Store;
        if (
            replace
            || !store.TryGetValue(id, out var current)
            || current is not JsonObject existing
        )
            store[id] = body.DeepClone();
        else
            foreach (var (key, value) in body.AsObject())
                existing[key] = value?.DeepClone();
        return RecordWrite(SchemaStore(kind).Tag, id, body);
    }

    // ── Content snapshot raw-JSON access (IContentSnapshotClient, #100) ─────────
    // Mirrors the schema harness: DocumentTree drives enumeration, DocumentRaw hands out canned
    // bodies keyed by id, and creates/updates land in the shared RawWrites (kind "document").

    /// <summary>The document placements returned by <see cref="GetDocumentTreeAsync"/> (pre-order).</summary>
    public List<ContentTreeNode> DocumentTree { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetDocumentRawAsync"/>, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> DocumentRaw { get; } = [];

    public Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetDocumentTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<IReadOnlyList<ContentTreeNode>>.Success(DocumentTree.ToList())
        );

    public Task<UmbracoResponse<JsonNode>> GetDocumentRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => Raw(DocumentRaw, id);

    public Task<UmbracoResponse<Empty>> CreateDocumentRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("document", null, body);

    public Task<UmbracoResponse<Empty>> UpdateDocumentRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("document", id, body);

    // ── Static files (IStaticFileClient, #105) ─────────────────────────────────
    // Configurable so command tests can drive StaticFileCommand end-to-end: each write records
    // the (kind, path/name, content) so a test can assert what the command layer sent.

    /// <summary>Recorded creates: the kind and the request the command built.</summary>
    public List<(
        StaticFileKind Kind,
        CreateStaticFileRequest Request
    )> StaticFilesCreated { get; } = [];

    /// <summary>Recorded updates: the kind, target path, and the new content the command sent.</summary>
    public List<(StaticFileKind Kind, string Path, string Content)> StaticFilesUpdated { get; } =
    [];

    /// <summary>Recorded deletes: the kind and target path.</summary>
    public List<(StaticFileKind Kind, string Path)> StaticFilesDeleted { get; } = [];

    /// <summary>
    /// The live static files per kind (#292), keyed by path in Umbraco's <c>/a/b</c> form: a null
    /// content marks a folder. The list and get methods read it, so an export walks it like the
    /// real tree. Empty by default, which lists nothing.
    /// </summary>
    public Dictionary<StaticFileKind, Dictionary<string, string?>> StaticFileTree { get; } =
        new()
        {
            [StaticFileKind.PartialView] = [],
            [StaticFileKind.Stylesheet] = [],
            [StaticFileKind.Script] = [],
        };

    /// <summary>When set, listing static files returns this failure (export error path).</summary>
    public UmbracoResponse<PagedResponse<StaticFileTreeItem>>? StaticFileListFailure { get; set; }

    /// <summary>Recorded folder creates (#238): the kind, name and parent path as passed.</summary>
    public List<(
        StaticFileKind Kind,
        string Name,
        string? ParentPath
    )> StaticFileFoldersCreated { get; } = [];

    /// <summary>Recorded folder deletes (#238): the kind and path.</summary>
    public List<(StaticFileKind Kind, string Path)> StaticFileFoldersDeleted { get; } = [];

    /// <summary>Every static-file write in call order, as "verb kind path", for ordering tests.</summary>
    public List<string> StaticFileWrites { get; } = [];

    /// <summary>
    /// Lists the direct children of <paramref name="parentPath"/> in <see cref="StaticFileTree"/>,
    /// ordered by path.
    /// </summary>
    /// <param name="kind">Which kind.</param>
    /// <param name="parentPath">The folder, or null for the root.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page.</returns>
    public Task<UmbracoResponse<PagedResponse<StaticFileTreeItem>>> GetStaticFilesAsync(
        StaticFileKind kind,
        string? parentPath = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    )
    {
        if (StaticFileListFailure is { } failure)
            return Task.FromResult(failure);
        var parent = parentPath is null ? "" : "/" + parentPath.Trim('/');
        var children = StaticFileTree[kind]
            .Where(e =>
                e.Key.StartsWith(parent + "/", StringComparison.Ordinal)
                && !e.Key[(parent.Length + 1)..].Contains('/')
            )
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => new StaticFileTreeItem
            {
                Path = e.Key,
                Name = e.Key[(e.Key.LastIndexOf('/') + 1)..],
                IsFolder = e.Value is null,
                HasChildren =
                    e.Value is null
                    && StaticFileTree[kind].Keys.Any(k => k.StartsWith(e.Key + "/")),
            })
            .ToList();
        return Task.FromResult(
            UmbracoResponse<PagedResponse<StaticFileTreeItem>>.Success(
                new PagedResponse<StaticFileTreeItem>
                {
                    Total = children.Count,
                    Items = [.. children.Skip(skip).Take(take)],
                }
            )
        );
    }

    /// <summary>
    /// Returns a file from <see cref="StaticFileTree"/> with its content, or, when the tree does
    /// not hold it, a bare record naming the path.
    /// </summary>
    /// <param name="kind">Which kind.</param>
    /// <param name="path">The file path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file.</returns>
    public Task<UmbracoResponse<StaticFileResponse>> GetStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<StaticFileResponse>.Success(
                StaticFileTree[kind].TryGetValue(path, out var content) && content is not null
                    ? new StaticFileResponse
                    {
                        Path = path,
                        Name = path[(path.LastIndexOf('/') + 1)..],
                        Content = content,
                    }
                    : new StaticFileResponse { Path = path, Name = path }
            )
        );

    /// <summary>Records a folder create.</summary>
    /// <param name="kind">Which kind.</param>
    /// <param name="name">The folder name.</param>
    /// <param name="parentPath">The parent path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The folder under the normalised path.</returns>
    public Task<UmbracoResponse<StaticFileFolderResponse>> CreateStaticFileFolderAsync(
        StaticFileKind kind,
        string name,
        string? parentPath,
        CancellationToken ct = default
    )
    {
        StaticFileFoldersCreated.Add((kind, name, parentPath));
        var parent = parentPath?.Trim('/');
        var path = string.IsNullOrEmpty(parent) ? $"/{name}" : $"/{parent}/{name}";
        StaticFileWrites.Add($"create-folder {kind} {path}");
        return Task.FromResult(
            UmbracoResponse<StaticFileFolderResponse>.Success(
                new StaticFileFolderResponse { Path = path, Name = name }
            )
        );
    }

    /// <summary>Records a folder delete.</summary>
    /// <param name="kind">Which kind.</param>
    /// <param name="path">The folder path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success.</returns>
    public Task<UmbracoResponse<Empty>> DeleteStaticFileFolderAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    )
    {
        StaticFileFoldersDeleted.Add((kind, path));
        StaticFileWrites.Add($"delete-folder {kind} {path}");
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    )
    {
        StaticFilesCreated.Add((kind, request));
        StaticFileWrites.Add(
            $"create {kind} /{(request.ParentPath is { } p ? p.Trim('/') + "/" : "")}{request.Name}"
        );
        return Task.FromResult(
            UmbracoResponse<StaticFileResponse>.Success(
                new StaticFileResponse
                {
                    Path = request.Name,
                    Name = request.Name,
                    Content = request.Content,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateStaticFileAsync(
        StaticFileKind kind,
        string path,
        UpdateStaticFileRequest request,
        CancellationToken ct = default
    )
    {
        StaticFilesUpdated.Add((kind, path, request.Content));
        StaticFileWrites.Add($"update {kind} {path}");
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    )
    {
        StaticFilesDeleted.Add((kind, path));
        StaticFileWrites.Add($"delete {kind} {path}");
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // ── Coverage resources: member groups, tags, cultures (#107) ───────────────

    /// <summary>Member groups the list method returns (seeded by a test).</summary>
    public List<MemberGroupResponse> MemberGroupList { get; } = [];

    /// <summary>Recorded member-group creates.</summary>
    public List<CreateMemberGroupRequest> MemberGroupsCreated { get; } = [];

    /// <summary>Recorded member-group deletes.</summary>
    public List<Guid> MemberGroupsDeleted { get; } = [];

    /// <summary>Tags the list method returns (seeded by a test).</summary>
    public List<TagResponse> TagList { get; } = [];

    /// <summary>Cultures the list method returns (seeded by a test).</summary>
    public List<CultureResponse> CultureList { get; } = [];

    public Task<UmbracoResponse<PagedResponse<MemberGroupResponse>>> GetMemberGroupsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<MemberGroupResponse>>.Success(
                new PagedResponse<MemberGroupResponse>
                {
                    Total = MemberGroupList.Count,
                    Items = MemberGroupList.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<MemberGroupResponse>> GetMemberGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            MemberGroupList.FirstOrDefault(g => g.Id == id) is { } g
                ? UmbracoResponse<MemberGroupResponse>.Success(g)
                : UmbracoResponse<MemberGroupResponse>.Failure(404, $"Not found: {id}")
        );

    public Task<UmbracoResponse<MemberGroupResponse>> CreateMemberGroupAsync(
        CreateMemberGroupRequest request,
        CancellationToken ct = default
    )
    {
        MemberGroupsCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<MemberGroupResponse>.Success(
                new MemberGroupResponse { Id = request.Id ?? Guid.NewGuid(), Name = request.Name }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateMemberGroupAsync(
        Guid id,
        UpdateMemberGroupRequest request,
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));

    public Task<UmbracoResponse<Empty>> DeleteMemberGroupAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        MemberGroupsDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<TagResponse>>> GetTagsAsync(
        string? tagGroup = null,
        string? culture = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<TagResponse>>.Success(
                new PagedResponse<TagResponse>
                {
                    Total = TagList.Count,
                    Items = TagList.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<PagedResponse<CultureResponse>>> GetCulturesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<CultureResponse>>.Success(
                new PagedResponse<CultureResponse>
                {
                    Total = CultureList.Count,
                    Items = CultureList.Skip(skip).Take(take).ToList(),
                }
            )
        );

    // ── User administration: user groups, user data (#109) ─────────────────────

    /// <summary>User groups the list/get methods return (seeded by a test).</summary>
    public List<UserGroupResponse> UserGroupList { get; } = [];

    /// <summary>Recorded user-group creates.</summary>
    public List<CreateUserGroupRequest> UserGroupsCreated { get; } = [];

    /// <summary>Recorded user-group updates, in <c>(id, request)</c> order.</summary>
    public List<(Guid Id, UpdateUserGroupRequest Request)> UserGroupsUpdated { get; } = [];

    /// <summary>Recorded single user-group deletes.</summary>
    public List<Guid> UserGroupsDeleted { get; } = [];

    /// <summary>Recorded bulk user-group deletes (each call's id list).</summary>
    public List<IReadOnlyList<Guid>> UserGroupsBulkDeleted { get; } = [];

    /// <summary>Recorded add-users calls, in <c>(groupId, userIds)</c> order.</summary>
    public List<(Guid GroupId, IReadOnlyList<Guid> UserIds)> UserGroupUsersAdded { get; } = [];

    /// <summary>Recorded remove-users calls, in <c>(groupId, userIds)</c> order.</summary>
    public List<(Guid GroupId, IReadOnlyList<Guid> UserIds)> UserGroupUsersRemoved { get; } = [];

    public Task<UmbracoResponse<PagedResponse<UserGroupResponse>>> GetUserGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<UserGroupResponse>>.Success(
                new PagedResponse<UserGroupResponse>
                {
                    Total = UserGroupList.Count,
                    Items = UserGroupList.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<UserGroupResponse>> GetUserGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UserGroupList.FirstOrDefault(g => g.Id == id) is { } g
                ? UmbracoResponse<UserGroupResponse>.Success(g)
                : UmbracoResponse<UserGroupResponse>.Failure(404, $"Not found: {id}")
        );

    public Task<UmbracoResponse<UserGroupResponse>> CreateUserGroupAsync(
        CreateUserGroupRequest request,
        CancellationToken ct = default
    )
    {
        UserGroupsCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<UserGroupResponse>.Success(
                new UserGroupResponse
                {
                    Id = request.Id ?? Guid.NewGuid(),
                    Alias = request.Alias,
                    Name = request.Name,
                    Icon = request.Icon,
                    Description = request.Description,
                    Sections = request.Sections,
                    Languages = request.Languages,
                    FallbackPermissions = request.FallbackPermissions,
                    HasAccessToAllLanguages = request.HasAccessToAllLanguages,
                    DocumentRootAccess = request.DocumentRootAccess,
                    MediaRootAccess = request.MediaRootAccess,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateUserGroupAsync(
        Guid id,
        UpdateUserGroupRequest request,
        CancellationToken ct = default
    )
    {
        UserGroupsUpdated.Add((id, request));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteUserGroupAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        UserGroupsDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteUserGroupsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    )
    {
        UserGroupsBulkDeleted.Add(ids);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> AddUsersToGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    )
    {
        UserGroupUsersAdded.Add((id, userIds));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> RemoveUsersFromGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    )
    {
        UserGroupUsersRemoved.Add((id, userIds));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>User-data entries the list/get methods return (seeded by a test).</summary>
    public List<UserDataResponse> UserDataList { get; } = [];

    /// <summary>Recorded user-data creates.</summary>
    public List<CreateUserDataRequest> UserDataCreated { get; } = [];

    /// <summary>Recorded user-data updates.</summary>
    public List<UpdateUserDataRequest> UserDataUpdated { get; } = [];

    /// <summary>Recorded user-data deletes.</summary>
    public List<Guid> UserDataDeleted { get; } = [];

    public Task<UmbracoResponse<PagedResponse<UserDataResponse>>> GetUserDataAsync(
        string? group = null,
        string? identifier = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        var filtered = UserDataList
            .Where(d => group is null || d.Group == group)
            .Where(d => identifier is null || d.Identifier == identifier)
            .ToList();
        return Task.FromResult(
            UmbracoResponse<PagedResponse<UserDataResponse>>.Success(
                new PagedResponse<UserDataResponse>
                {
                    Total = filtered.Count,
                    Items = filtered.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    public Task<UmbracoResponse<UserDataResponse>> GetUserDataByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UserDataList.FirstOrDefault(d => d.Key == id) is { } d
                ? UmbracoResponse<UserDataResponse>.Success(d)
                : UmbracoResponse<UserDataResponse>.Failure(404, $"Not found: {id}")
        );

    public Task<UmbracoResponse<UserDataResponse>> CreateUserDataAsync(
        CreateUserDataRequest request,
        CancellationToken ct = default
    )
    {
        UserDataCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<UserDataResponse>.Success(
                new UserDataResponse
                {
                    Key = request.Key ?? Guid.NewGuid(),
                    Group = request.Group,
                    Identifier = request.Identifier,
                    Value = request.Value,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateUserDataAsync(
        UpdateUserDataRequest request,
        CancellationToken ct = default
    )
    {
        UserDataUpdated.Add(request);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteUserDataAsync(Guid id, CancellationToken ct = default)
    {
        UserDataDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // ── Document blueprints (#113) ──────────────────────────────────────────────

    /// <summary>Blueprint tree items the list method returns (seeded by a test).</summary>
    public List<DocumentBlueprintTreeItem> BlueprintTreeItems { get; } = [];

    /// <summary>The (parentId, skip, take) of the last list call.</summary>
    public (Guid? ParentId, int Skip, int Take)? LastBlueprintListArgs { get; private set; }

    /// <summary>Recorded blueprint creates.</summary>
    public List<CreateDocumentBlueprintRequest> BlueprintsCreated { get; } = [];

    /// <summary>Recorded from-document blueprint creates.</summary>
    public List<CreateBlueprintFromDocumentRequest> BlueprintsFromDocumentCreated { get; } = [];

    /// <summary>Recorded blueprint updates, in <c>(id, request)</c> order.</summary>
    public List<(Guid Id, UpdateDocumentBlueprintRequest Request)> BlueprintsUpdated { get; } = [];

    /// <summary>Recorded blueprint deletes.</summary>
    public List<Guid> BlueprintsDeleted { get; } = [];

    /// <summary>Recorded blueprint moves, in <c>(id, target)</c> order.</summary>
    public List<(Guid Id, Guid? Target)> BlueprintsMoved { get; } = [];

    /// <summary>Recorded folder creates.</summary>
    public List<CreateBlueprintFolderRequest> BlueprintFoldersCreated { get; } = [];

    /// <summary>Recorded folder updates, in <c>(id, name)</c> order.</summary>
    public List<(Guid Id, string Name)> BlueprintFoldersUpdated { get; } = [];

    /// <summary>Recorded folder deletes.</summary>
    public List<Guid> BlueprintFoldersDeleted { get; } = [];

    public Task<
        UmbracoResponse<PagedResponse<DocumentBlueprintTreeItem>>
    > GetDocumentBlueprintsAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastBlueprintListArgs = (parentId, skip, take);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<DocumentBlueprintTreeItem>>.Success(
                new PagedResponse<DocumentBlueprintTreeItem>
                {
                    Total = BlueprintTreeItems.Count,
                    Items = BlueprintTreeItems.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    public Task<UmbracoResponse<JsonNode>> GetDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<JsonNode>.Success(new JsonObject { ["id"] = id.ToString() })
        );

    public Task<UmbracoResponse<JsonNode>> ScaffoldDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<JsonNode>.Success(
                new JsonObject { ["id"] = id.ToString(), ["scaffold"] = true }
            )
        );

    public Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintAsync(
        CreateDocumentBlueprintRequest request,
        CancellationToken ct = default
    )
    {
        BlueprintsCreated.Add(request);
        var id = request.Id ?? Guid.NewGuid();
        return Task.FromResult(
            UmbracoResponse<JsonNode>.Success(new JsonObject { ["id"] = id.ToString() })
        );
    }

    public Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintFromDocumentAsync(
        CreateBlueprintFromDocumentRequest request,
        CancellationToken ct = default
    )
    {
        BlueprintsFromDocumentCreated.Add(request);
        var id = request.Id ?? Guid.NewGuid();
        return Task.FromResult(
            UmbracoResponse<JsonNode>.Success(new JsonObject { ["id"] = id.ToString() })
        );
    }

    /// <summary>The replace flag of the last blueprint update (#242).</summary>
    public bool? LastBlueprintReplace { get; private set; }

    public Task<UmbracoResponse<Empty>> UpdateDocumentBlueprintAsync(
        Guid id,
        UpdateDocumentBlueprintRequest request,
        bool replace = false,
        CancellationToken ct = default
    )
    {
        LastBlueprintReplace = replace;
        BlueprintsUpdated.Add((id, request));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        BlueprintsDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> MoveDocumentBlueprintAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    )
    {
        BlueprintsMoved.Add((id, targetId));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<BlueprintFolderResponse>> GetBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<BlueprintFolderResponse>.Success(
                new BlueprintFolderResponse { Id = id, Name = "Folder" }
            )
        );

    public Task<UmbracoResponse<BlueprintFolderResponse>> CreateBlueprintFolderAsync(
        CreateBlueprintFolderRequest request,
        CancellationToken ct = default
    )
    {
        BlueprintFoldersCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<BlueprintFolderResponse>.Success(
                new BlueprintFolderResponse
                {
                    Id = request.Id ?? Guid.NewGuid(),
                    Name = request.Name,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateBlueprintFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    )
    {
        BlueprintFoldersUpdated.Add((id, name));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        BlueprintFoldersDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // ── Diagnostics (#115) ──────────────────────────────────────────────────────

    // Server (read-only; return simple canned objects).
    public Task<UmbracoResponse<ServerStatusResponse>> GetServerStatusAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<ServerStatusResponse>.Success(
                new ServerStatusResponse { ServerStatus = "Run" }
            )
        );

    public Task<UmbracoResponse<ServerInformationResponse>> GetServerInformationAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<ServerInformationResponse>.Success(
                new ServerInformationResponse { Version = "14.0.0" }
            )
        );

    /// <summary>The version the fake reports from <see cref="GetServerVersionAsync"/> (default matches the fake server info).</summary>
    public string? ServerVersion { get; set; } = "14.0.0";

    public Task<string?> GetServerVersionAsync(CancellationToken ct = default) =>
        Task.FromResult(ServerVersion);

    public Task<UmbracoResponse<ServerConfigurationResponse>> GetServerConfigurationAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<ServerConfigurationResponse>.Success(new ServerConfigurationResponse())
        );

    public Task<
        UmbracoResponse<IReadOnlyList<ServerTroubleshootingItem>>
    > GetServerTroubleshootingAsync(CancellationToken ct = default) =>
        Task.FromResult(UmbracoResponse<IReadOnlyList<ServerTroubleshootingItem>>.Success([]));

    // Health.
    /// <summary>Health-check groups the list method returns (seeded by a test).</summary>
    public List<HealthCheckGroupSummary> HealthGroups { get; } = [];

    /// <summary>Group names passed to <see cref="RunHealthCheckGroupAsync"/>, in order.</summary>
    public List<string> HealthGroupsRun { get; } = [];

    public Task<UmbracoResponse<PagedResponse<HealthCheckGroupSummary>>> GetHealthCheckGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<HealthCheckGroupSummary>>.Success(
                new PagedResponse<HealthCheckGroupSummary>
                {
                    Total = HealthGroups.Count,
                    Items = HealthGroups.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<HealthCheckGroupDetail>> GetHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<HealthCheckGroupDetail>.Success(
                new HealthCheckGroupDetail { Name = name }
            )
        );

    public Task<UmbracoResponse<HealthCheckRunResult>> RunHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    )
    {
        HealthGroupsRun.Add(name);
        return Task.FromResult(
            UmbracoResponse<HealthCheckRunResult>.Success(new HealthCheckRunResult())
        );
    }

    // Log viewer.
    /// <summary>Log messages the list method returns (seeded by a test).</summary>
    public List<LogMessageResponse> LogMessages { get; } = [];

    /// <summary>The (skip, take, levels, filter, start, end, descending) of the last log query.</summary>
    public (
        int Skip,
        int Take,
        IReadOnlyList<LogLevel>? Levels,
        string? Filter,
        DateTimeOffset? Start,
        DateTimeOffset? End,
        bool Descending
    )? LastLogQuery { get; private set; }

    /// <summary>Recorded saved-search creates, in <c>(name, query)</c> order.</summary>
    public List<(string Name, string Query)> SavedSearchesCreated { get; } = [];

    /// <summary>Recorded saved-search deletes.</summary>
    public List<string> SavedSearchesDeleted { get; } = [];

    public Task<UmbracoResponse<PagedResponse<LogMessageResponse>>> GetLogsAsync(
        int skip = 0,
        int take = 100,
        IReadOnlyList<LogLevel>? levels = null,
        string? filterExpression = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool descending = true,
        CancellationToken ct = default
    )
    {
        LastLogQuery = (skip, take, levels, filterExpression, startDate, endDate, descending);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<LogMessageResponse>>.Success(
                new PagedResponse<LogMessageResponse>
                {
                    Total = LogMessages.Count,
                    Items = LogMessages.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    public Task<UmbracoResponse<PagedResponse<LoggerLevelResponse>>> GetLogLevelsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<LoggerLevelResponse>>.Success(
                new PagedResponse<LoggerLevelResponse> { Total = 0, Items = [] }
            )
        );

    public Task<UmbracoResponse<LogLevelCounts>> GetLogLevelCountsAsync(
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<LogLevelCounts>.Success(new LogLevelCounts()));

    public Task<UmbracoResponse<PagedResponse<LogTemplateResponse>>> GetLogMessageTemplatesAsync(
        int skip = 0,
        int take = 100,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<LogTemplateResponse>>.Success(
                new PagedResponse<LogTemplateResponse> { Total = 0, Items = [] }
            )
        );

    public Task<UmbracoResponse<PagedResponse<SavedLogSearchResponse>>> GetSavedLogSearchesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<SavedLogSearchResponse>>.Success(
                new PagedResponse<SavedLogSearchResponse> { Total = 0, Items = [] }
            )
        );

    public Task<UmbracoResponse<SavedLogSearchResponse>> CreateSavedLogSearchAsync(
        string name,
        string query,
        CancellationToken ct = default
    )
    {
        SavedSearchesCreated.Add((name, query));
        return Task.FromResult(
            UmbracoResponse<SavedLogSearchResponse>.Success(
                new SavedLogSearchResponse { Name = name, Query = query }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> DeleteSavedLogSearchAsync(
        string name,
        CancellationToken ct = default
    )
    {
        SavedSearchesDeleted.Add(name);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // Models builder.
    /// <summary>How many times <see cref="BuildModelsAsync"/> was called.</summary>
    public int ModelsBuiltCount { get; private set; }

    public Task<UmbracoResponse<ModelsBuilderDashboard>> GetModelsBuilderDashboardAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<ModelsBuilderDashboard>.Success(
                new ModelsBuilderDashboard { Mode = "InMemoryAuto" }
            )
        );

    public Task<UmbracoResponse<ModelsBuilderStatus>> GetModelsBuilderStatusAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<ModelsBuilderStatus>.Success(
                new ModelsBuilderStatus { Status = "Current" }
            )
        );

    public Task<UmbracoResponse<Empty>> BuildModelsAsync(CancellationToken ct = default)
    {
        ModelsBuiltCount++;
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    // Manifest.
    /// <summary>Manifests the list method returns (seeded by a test).</summary>
    public List<ManifestResponse> Manifests { get; } = [];

    /// <summary>The scope of the last manifest list call.</summary>
    public ManifestScope? LastManifestScope { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<ManifestResponse>>> GetManifestsAsync(
        ManifestScope scope = ManifestScope.All,
        CancellationToken ct = default
    )
    {
        LastManifestScope = scope;
        return Task.FromResult(
            UmbracoResponse<IReadOnlyList<ManifestResponse>>.Success(Manifests.ToList())
        );
    }

    // ── Redirects + relations (#118) ────────────────────────────────────────────

    /// <summary>Redirects the list method returns (seeded by a test).</summary>
    public List<RedirectResponse> Redirects { get; } = [];

    /// <summary>The content key of the last for-content list call, or null for a global list.</summary>
    public Guid? LastRedirectContentKey { get; private set; }

    /// <summary>The filter of the last global redirect list call.</summary>
    public string? LastRedirectFilter { get; private set; }

    /// <summary>Recorded redirect deletes.</summary>
    public List<Guid> RedirectsDeleted { get; } = [];

    /// <summary>Recorded tracking-toggle calls (the enabled state each set).</summary>
    public List<bool> RedirectTrackingSet { get; } = [];

    /// <summary>Relation types the list/get methods return (seeded by a test).</summary>
    public List<RelationTypeResponse> RelationTypes { get; } = [];

    /// <summary>Relations the list method returns (seeded by a test).</summary>
    public List<RelationResponse> Relations { get; } = [];

    /// <summary>The relation-type id of the last relation list call.</summary>
    public Guid? LastRelationTypeQueried { get; private set; }

    public Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsAsync(
        string? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastRedirectFilter = filter;
        LastRedirectContentKey = null;
        return Task.FromResult(
            UmbracoResponse<PagedResponse<RedirectResponse>>.Success(
                new PagedResponse<RedirectResponse>
                {
                    Total = Redirects.Count,
                    Items = Redirects.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    public Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsForContentAsync(
        Guid contentKey,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastRedirectContentKey = contentKey;
        return Task.FromResult(
            UmbracoResponse<PagedResponse<RedirectResponse>>.Success(
                new PagedResponse<RedirectResponse>
                {
                    Total = Redirects.Count,
                    Items = Redirects.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    public Task<UmbracoResponse<RedirectStatusResponse>> GetRedirectStatusAsync(
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<RedirectStatusResponse>.Success(
                new RedirectStatusResponse { Enabled = true, UserIsAdmin = true }
            )
        );

    public Task<UmbracoResponse<Empty>> DeleteRedirectAsync(Guid id, CancellationToken ct = default)
    {
        RedirectsDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> SetRedirectTrackingAsync(
        bool enabled,
        CancellationToken ct = default
    )
    {
        RedirectTrackingSet.Add(enabled);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<RelationTypeResponse>>> GetRelationTypesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<RelationTypeResponse>>.Success(
                new PagedResponse<RelationTypeResponse>
                {
                    Total = RelationTypes.Count,
                    Items = RelationTypes.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<RelationTypeResponse>> GetRelationTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            RelationTypes.FirstOrDefault(t => t.Id == id) is { } t
                ? UmbracoResponse<RelationTypeResponse>.Success(t)
                : UmbracoResponse<RelationTypeResponse>.Failure(404, $"Not found: {id}")
        );

    public Task<UmbracoResponse<PagedResponse<RelationResponse>>> GetRelationsByTypeAsync(
        Guid relationTypeId,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastRelationTypeQueried = relationTypeId;
        return Task.FromResult(
            UmbracoResponse<PagedResponse<RelationResponse>>.Success(
                new PagedResponse<RelationResponse>
                {
                    Total = Relations.Count,
                    Items = Relations.Skip(skip).Take(take).ToList(),
                }
            )
        );
    }

    // ── Coverage finale: Examine, imaging, data-type advanced, property-type (#121) ──

    /// <summary>Indexes the list method returns (seeded by a test).</summary>
    public List<IndexResponse> Indexes { get; } = [];

    /// <summary>Index names passed to <see cref="RebuildIndexAsync"/>, in order.</summary>
    public List<string> IndexesRebuilt { get; } = [];

    /// <summary>Searchers the list method returns (seeded by a test).</summary>
    public List<SearcherResponse> Searchers { get; } = [];

    /// <summary>The (searcher, term) of the last query.</summary>
    public (string Name, string Term)? LastSearcherQuery { get; private set; }

    public Task<UmbracoResponse<PagedResponse<IndexResponse>>> GetIndexersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<IndexResponse>>.Success(
                new PagedResponse<IndexResponse>
                {
                    Total = Indexes.Count,
                    Items = Indexes.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<IndexResponse>> GetIndexerAsync(
        string name,
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<IndexResponse>.Success(new IndexResponse { Name = name }));

    public Task<UmbracoResponse<Empty>> RebuildIndexAsync(
        string name,
        CancellationToken ct = default
    )
    {
        IndexesRebuilt.Add(name);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<PagedResponse<SearcherResponse>>> GetSearchersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<SearcherResponse>>.Success(
                new PagedResponse<SearcherResponse>
                {
                    Total = Searchers.Count,
                    Items = Searchers.Skip(skip).Take(take).ToList(),
                }
            )
        );

    public Task<UmbracoResponse<PagedResponse<SearchResultResponse>>> QuerySearcherAsync(
        string name,
        string term,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        LastSearcherQuery = (name, term);
        return Task.FromResult(
            UmbracoResponse<PagedResponse<SearchResultResponse>>.Success(
                new PagedResponse<SearchResultResponse> { Total = 0, Items = [] }
            )
        );
    }

    /// <summary>The (ids, width, height, mode, format) of the last resize-urls call.</summary>
    public (
        IReadOnlyList<Guid> Ids,
        int? Width,
        int? Height,
        ImageResizeMode? Mode,
        string? Format
    )? LastResizeUrls { get; private set; }

    public Task<UmbracoResponse<IReadOnlyList<MediaResizeUrlResponse>>> GetResizeUrlsAsync(
        IReadOnlyList<Guid> mediaIds,
        int? width = null,
        int? height = null,
        ImageResizeMode? mode = null,
        string? format = null,
        CancellationToken ct = default
    )
    {
        LastResizeUrls = (mediaIds, width, height, mode, format);
        return Task.FromResult(UmbracoResponse<IReadOnlyList<MediaResizeUrlResponse>>.Success([]));
    }

    /// <summary>Recorded data-type is-used queries.</summary>
    public List<Guid> DataTypeIsUsedQueried { get; } = [];

    /// <summary>Recorded data-type copies, in <c>(id, target)</c> order.</summary>
    public List<(Guid Id, Guid? Target)> DataTypesCopied { get; } = [];

    /// <summary>Recorded data-type moves, in <c>(id, target)</c> order.</summary>
    public List<(Guid Id, Guid? Target)> DataTypesMoved { get; } = [];

    /// <summary>Recorded data-type folder creates.</summary>
    public List<CreateDataTypeFolderRequest> DataTypeFoldersCreated { get; } = [];

    /// <summary>Recorded data-type folder deletes.</summary>
    public List<Guid> DataTypeFoldersDeleted { get; } = [];

    /// <summary>
    /// Data types that report as in use, each with the property references
    /// <c>referenced-by</c> returns for it (document type name, property name) (#246).
    /// </summary>
    public Dictionary<Guid, List<(string DocumentType, string Property)>> UsedDataTypes { get; } =
    [];

    public Task<UmbracoResponse<bool>> IsDataTypeUsedAsync(Guid id, CancellationToken ct = default)
    {
        DataTypeIsUsedQueried.Add(id);
        return Task.FromResult(UmbracoResponse<bool>.Success(UsedDataTypes.ContainsKey(id)));
    }

    public Task<UmbracoResponse<JsonNode>> GetDataTypeReferencedByRawAsync(
        Guid id,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    )
    {
        // The Management API's shape: {total, items: [{$type, documentType: {name, ...}, name}]}.
        var references = UsedDataTypes.GetValueOrDefault(id) ?? [];
        var items = new JsonArray();
        foreach (var (documentType, property) in references)
            items.Add(
                new JsonObject
                {
                    ["$type"] = "DocumentTypePropertyTypeReferenceResponseModel",
                    ["documentType"] = new JsonObject { ["name"] = documentType },
                    ["name"] = property,
                }
            );
        return Task.FromResult(
            UmbracoResponse<JsonNode>.Success(
                new JsonObject { ["total"] = references.Count, ["items"] = items }
            )
        );
    }

    public Task<UmbracoResponse<DataTypeResponse>> CopyDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    )
    {
        DataTypesCopied.Add((id, targetId));
        return Task.FromResult(
            UmbracoResponse<DataTypeResponse>.Success(new DataTypeResponse { Id = Guid.NewGuid() })
        );
    }

    public Task<UmbracoResponse<Empty>> MoveDataTypeAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    )
    {
        DataTypesMoved.Add((id, targetId));
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<DataTypeFolderResponse>> GetDataTypeFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<DataTypeFolderResponse>.Success(
                new DataTypeFolderResponse { Id = id, Name = "Folder" }
            )
        );

    public Task<UmbracoResponse<DataTypeFolderResponse>> CreateDataTypeFolderAsync(
        CreateDataTypeFolderRequest request,
        CancellationToken ct = default
    )
    {
        DataTypeFoldersCreated.Add(request);
        return Task.FromResult(
            UmbracoResponse<DataTypeFolderResponse>.Success(
                new DataTypeFolderResponse
                {
                    Id = request.Id ?? Guid.NewGuid(),
                    Name = request.Name,
                }
            )
        );
    }

    public Task<UmbracoResponse<Empty>> UpdateDataTypeFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    ) => Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));

    public Task<UmbracoResponse<Empty>> DeleteDataTypeFolderAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        DataTypeFoldersDeleted.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    /// <summary>The (contentTypeId, alias) of the last property-type is-used query.</summary>
    public (Guid ContentTypeId, string Alias)? LastPropertyTypeUsedQuery { get; private set; }

    public Task<UmbracoResponse<bool>> IsPropertyTypeUsedAsync(
        Guid contentTypeId,
        string propertyAlias,
        CancellationToken ct = default
    )
    {
        LastPropertyTypeUsedQuery = (contentTypeId, propertyAlias);
        return Task.FromResult(UmbracoResponse<bool>.Success(true));
    }

    // ── references (#250 Phase 3) ─────────────────────────────────────────────

    /// <summary>
    /// What each (kind, alias/name/key) resolves to. A GUID always resolves to itself, as in the
    /// real client; anything not listed here is a 404.
    /// </summary>
    public Dictionary<(EntityKind Kind, string Reference), Guid> References { get; } = [];

    /// <summary>Every non-GUID reference resolved, in order.</summary>
    public List<(EntityKind Kind, string Reference)> Resolved { get; } = [];

    public Task<UmbracoResponse<Guid>> ResolveIdAsync(
        EntityKind kind,
        string reference,
        CancellationToken ct = default
    )
    {
        if (Guid.TryParse(reference, out var id))
            return Task.FromResult(UmbracoResponse<Guid>.Success(id));
        Resolved.Add((kind, reference));
        return Task.FromResult(
            References.TryGetValue((kind, reference), out var found)
                ? UmbracoResponse<Guid>.Success(found)
                : UmbracoResponse<Guid>.Failure(404, $"No {kind} '{reference}'.")
        );
    }
}
