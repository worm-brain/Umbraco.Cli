namespace Umbraco.Cli.Client;

/// <summary>
/// Typed client for the Umbraco Management API (v1).
/// Covers the operations exposed by the CLI commands.
///
/// This interface can be backed by either the hand-crafted HttpClient
/// implementation (default) or a Kiota-generated client once you have
/// a running Umbraco instance:
///   kiota generate --language csharp \
///     --openapi http://localhost:5000/umbraco/swagger/management/swagger.json \
///     --output src/Umbraco.Cli.Client/Generated \
///     --namespace-name Umbraco.Cli.Client \
///     --class-name UmbracoManagementClient
/// </summary>
public interface IUmbracoManagementClient
{
    // ── Auth / Identity ──────────────────────────────────────────────────────

    Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken ct = default);

    // ── Content ──────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(CreateContentRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(Guid id, UpdateContentRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteContentAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<object>> PublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default);

    Task<UmbracoResponse<object>> UnpublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default);

    // ── Media ────────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(Guid parentId, string name, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteMediaAsync(Guid id, CancellationToken ct = default);

    // ── Document Types ───────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteDocumentTypeAsync(Guid id, CancellationToken ct = default);

    // ── Data Types ───────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(Guid id, CancellationToken ct = default);

    // ── Languages ────────────────────────────────────────────────────────────

    Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(CancellationToken ct = default);

    Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteLanguageAsync(string isoCode, CancellationToken ct = default);

    // ── Templates ────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(string alias, CancellationToken ct = default);

    // ── Members ──────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null, int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(CreateMemberRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteMemberAsync(Guid id, CancellationToken ct = default);

    // ── Users ─────────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<object>> InviteUserAsync(InviteUserRequest request, CancellationToken ct = default);

    // ── Dictionary ───────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(string key, CancellationToken ct = default);

    Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(CreateDictionaryItemRequest request, CancellationToken ct = default);

    // ── Webhooks ──────────────────────────────────────────────────────────────

    Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<object>> DeleteWebhookAsync(Guid id, CancellationToken ct = default);
}
