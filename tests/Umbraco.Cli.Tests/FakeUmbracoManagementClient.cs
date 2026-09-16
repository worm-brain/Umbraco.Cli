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

    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    // Configurable exemplar used by the executor tests.
    public UmbracoResponse<ContentItemResponse>? ContentByIdResponse { get; set; }
    public Guid? LastRequestedId { get; private set; }

    public Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(
        Guid id,
        CancellationToken ct = default
    )
    {
        LastRequestedId = id;
        return Task.FromResult(
            ContentByIdResponse
                ?? throw new InvalidOperationException("ContentByIdResponse not configured.")
        );
    }

    public Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(
        CreateContentRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    // Optional per-id handlers used by the bulk-operation tests (#85). When null the method
    // throws (flagging an unintended call), matching the fake's default behaviour.
    public Func<Guid, UmbracoResponse<Empty>>? DeleteContentHandler { get; set; }
    public Func<Guid, UmbracoResponse<Empty>>? PublishContentHandler { get; set; }

    /// <summary>Ids passed to <see cref="DeleteContentAsync"/> / <see cref="PublishContentAsync"/>, in order.</summary>
    public List<Guid> CalledIds { get; } = [];

    public Task<UmbracoResponse<Empty>> DeleteContentAsync(Guid id, CancellationToken ct = default)
    {
        if (DeleteContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        return Task.FromResult(DeleteContentHandler(id));
    }

    public Task<UmbracoResponse<Empty>> PublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    )
    {
        if (PublishContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        return Task.FromResult(PublishContentHandler(id));
    }

    public Func<Guid, UmbracoResponse<Empty>>? UnpublishContentHandler { get; set; }

    public Task<UmbracoResponse<Empty>> UnpublishContentAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        CancellationToken ct = default
    )
    {
        if (UnpublishContentHandler is null)
            throw new NotImplementedException();
        CalledIds.Add(id);
        return Task.FromResult(UnpublishContentHandler(id));
    }

    public Task<UmbracoResponse<Empty>> TrashContentAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> EmptyContentRecycleBinAsync(
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> MoveContentAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> CopyContentAsync(
        Guid id,
        Guid? parentId = null,
        bool includeDescendants = false,
        bool relateToOriginal = false,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> PublishContentWithDescendantsAsync(
        Guid id,
        IEnumerable<string>? cultures = null,
        bool includeUnpublishedDescendants = false,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<DocumentVersionResponse>>> GetDocumentVersionsAsync(
        Guid documentId,
        string? culture = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> RollbackDocumentVersionAsync(
        Guid versionId,
        string? culture = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

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

    public Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid? parentId,
        string name,
        Stream fileStream,
        string fileName,
        string contentType,
        string mediaType,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteMediaAsync(Guid id, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> TrashMediaAsync(Guid id, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> RestoreMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> EmptyMediaRecycleBinAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> MoveMediaAsync(
        Guid id,
        Guid? parentId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<MediaTypeResponse>>> GetMediaTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MediaTypeResponse>> GetMediaTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MediaTypeResponse>> CreateMediaTypeAsync(
        CreateMediaTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteMediaTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

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

    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(
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
        CancellationToken ct = default
    ) => Page(DataTypeList, skip, take);

    public Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<DataTypeResponse>> CreateDataTypeAsync(
        CreateDataTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> UpdateDataTypeAsync(
        Guid id,
        UpdateDataTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteDataTypeAsync(Guid id, CancellationToken ct = default)
    {
        SchemaDeletedIds.Add(id);
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(
        CreateLanguageRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<LanguageResponse>> UpdateLanguageAsync(
        string isoCode,
        UpdateLanguageRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteLanguageAsync(
        string isoCode,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => Page(TemplateList, skip, take);

    public Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(
        string alias,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<TemplateResponse>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> UpdateTemplateAsync(
        Guid id,
        UpdateTemplateRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

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

    public Task<UmbracoResponse<MemberResponse>> UpdateMemberAsync(
        Guid id,
        UpdateMemberRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteMemberAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<MemberTypeResponse>>> GetMemberTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberTypeResponse>> GetMemberTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberTypeResponse>> CreateMemberTypeAsync(
        CreateMemberTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> UpdateMemberTypeAsync(
        Guid id,
        UpdateMemberTypeRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteMemberTypeAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(
        string key,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(
        CreateDictionaryItemRequest request,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<UmbracoResponse<Empty>> DeleteDictionaryItemAsync(
        Guid id,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

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

    // ── Schema raw-JSON access (ISchemaClient, #68) ────────────────────────────
    // Backing stores let export tests hand out canned bodies (keyed by id) and apply tests
    // record every write. A recorded write keeps the entity kind, the target id (null for a
    // create), and the exact body sent, so a test can assert both the plan and the payload.

    /// <summary>Canned raw bodies returned by <see cref="GetDocumentTypeRawAsync"/>, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> DocumentTypeRaw { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetDataTypeRawAsync"/>, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> DataTypeRaw { get; } = [];

    /// <summary>Canned raw bodies returned by <see cref="GetTemplateRawAsync"/>, keyed by id.</summary>
    public Dictionary<Guid, JsonNode> TemplateRaw { get; } = [];

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

    public Task<UmbracoResponse<JsonNode>> GetDocumentTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => Raw(DocumentTypeRaw, id);

    public Task<UmbracoResponse<JsonNode>> GetDataTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => Raw(DataTypeRaw, id);

    public Task<UmbracoResponse<JsonNode>> GetTemplateRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => Raw(TemplateRaw, id);

    public Task<UmbracoResponse<Empty>> CreateDocumentTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("documentType", null, body);

    public Task<UmbracoResponse<Empty>> UpdateDocumentTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("documentType", id, body);

    public Task<UmbracoResponse<Empty>> CreateDataTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("dataType", null, body);

    public Task<UmbracoResponse<Empty>> UpdateDataTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("dataType", id, body);

    public Task<UmbracoResponse<Empty>> CreateTemplateRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("template", null, body);

    public Task<UmbracoResponse<Empty>> UpdateTemplateRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) => RecordWrite("template", id, body);

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

    public Task<UmbracoResponse<PagedResponse<StaticFileTreeItem>>> GetStaticFilesAsync(
        StaticFileKind kind,
        string? parentPath = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<PagedResponse<StaticFileTreeItem>>.Success(
                new PagedResponse<StaticFileTreeItem> { Total = 0, Items = [] }
            )
        );

    public Task<UmbracoResponse<StaticFileResponse>> GetStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            UmbracoResponse<StaticFileResponse>.Success(
                new StaticFileResponse { Path = path, Name = path }
            )
        );

    public Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    )
    {
        StaticFilesCreated.Add((kind, request));
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
        return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
    }

    public Task<UmbracoResponse<Empty>> DeleteStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    )
    {
        StaticFilesDeleted.Add((kind, path));
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

    public Task<UmbracoResponse<Empty>> UpdateDocumentBlueprintAsync(
        Guid id,
        UpdateDocumentBlueprintRequest request,
        CancellationToken ct = default
    )
    {
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
}
