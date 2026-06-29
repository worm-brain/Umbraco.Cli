using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// In-memory adapter for <see cref="IUmbracoManagementClient"/>. The seam from #26 lets
/// the executor run against canned responses instead of real HTTP. Only the members a
/// test needs are configured; the rest throw to flag unintended calls.
/// </summary>
internal sealed class FakeUmbracoManagementClient : IUmbracoManagementClient
{
    public Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync( Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    // Configurable exemplar used by the executor tests.
    public UmbracoResponse<ContentItemResponse>? ContentByIdResponse { get; set; }
    public Guid? LastRequestedId { get; private set; }
    public Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(Guid id, CancellationToken ct = default)
    {
        LastRequestedId = id;
        return Task.FromResult(ContentByIdResponse
            ?? throw new InvalidOperationException("ContentByIdResponse not configured."));
    }

    public Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(CreateContentRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(Guid id, UpdateContentRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteContentAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> PublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> UnpublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync( Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(Guid parentId, string name, Stream fileStream, string fileName, string contentType, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteMediaAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteDocumentTypeAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteLanguageAsync(string isoCode, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(string alias, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync( string? group = null, int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(CreateMemberRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteMemberAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> InviteUserAsync(InviteUserRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(string key, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(CreateDictionaryItemRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync( int skip = 0, int take = 20, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UmbracoResponse<object>> DeleteWebhookAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
}
