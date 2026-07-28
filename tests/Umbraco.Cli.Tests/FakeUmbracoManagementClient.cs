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
}
