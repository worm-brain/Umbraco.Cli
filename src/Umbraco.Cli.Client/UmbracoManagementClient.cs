using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

public sealed class UmbracoManagementClient : IUmbracoManagementClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public UmbracoManagementClient(HttpClient http)
    {
        _http = http;
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken ct = default) =>
        await GetAsync<CurrentUserResponse>("umbraco/management/api/v1/security/back-office/user-data", ct);

    // ── Content ──────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentAsync(
        Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var url = $"umbraco/management/api/v1/document?skip={skip}&take={take}";
        if (parentId.HasValue) url += $"&parentId={parentId.Value}";
        return await GetAsync<PagedResponse<ContentItemResponse>>(url, ct);
    }

    public async Task<UmbracoResponse<ContentItemResponse>> GetContentByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<ContentItemResponse>($"umbraco/management/api/v1/document/{id}", ct);

    public async Task<UmbracoResponse<ContentItemResponse>> CreateContentAsync(CreateContentRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateContentRequest, ContentItemResponse>("umbraco/management/api/v1/document", request, ct);

    public async Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(Guid id, UpdateContentRequest request, CancellationToken ct = default) =>
        await PutAsync<UpdateContentRequest, ContentItemResponse>($"umbraco/management/api/v1/document/{id}", request, ct);

    public async Task<UmbracoResponse<object>> DeleteContentAsync(Guid id, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/document/{id}", ct);

    public async Task<UmbracoResponse<object>> PublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default)
    {
        var schedules = (cultures ?? ["*"]).Select(c => new PublishSchedule { Culture = c });
        return await PutAsync<PublishContentRequest, object>(
            $"umbraco/management/api/v1/document/{id}/publish",
            new PublishContentRequest { PublishSchedules = schedules }, ct);
    }

    public async Task<UmbracoResponse<object>> UnpublishContentAsync(Guid id, IEnumerable<string>? cultures = null, CancellationToken ct = default)
    {
        var schedules = (cultures ?? ["*"]).Select(c => new PublishSchedule { Culture = c });
        return await PutAsync<PublishContentRequest, object>(
            $"umbraco/management/api/v1/document/{id}/unpublish",
            new PublishContentRequest { PublishSchedules = schedules }, ct);
    }

    // ── Media ────────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var url = $"umbraco/management/api/v1/media?skip={skip}&take={take}";
        if (parentId.HasValue) url += $"&parentId={parentId.Value}";
        return await GetAsync<PagedResponse<MediaItemResponse>>(url, ct);
    }

    public async Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<MediaItemResponse>($"umbraco/management/api/v1/media/{id}", ct);

    public async Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(
        Guid parentId, string name, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(parentId.ToString()), "parentId" },
            { new StringContent(name), "name" },
            { new StreamContent(fileStream) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } }, "file", fileName }
        };
        return await SendAsync<MediaItemResponse>(HttpMethod.Post, "umbraco/management/api/v1/media", form, ct);
    }

    public async Task<UmbracoResponse<object>> DeleteMediaAsync(Guid id, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/media/{id}", ct);

    // ── Document Types ───────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<DocumentTypeResponse>>> GetDocumentTypesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<DocumentTypeResponse>>($"umbraco/management/api/v1/document-type?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<DocumentTypeResponse>> GetDocumentTypeByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<DocumentTypeResponse>($"umbraco/management/api/v1/document-type/{id}", ct);

    public async Task<UmbracoResponse<DocumentTypeResponse>> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateDocumentTypeRequest, DocumentTypeResponse>("umbraco/management/api/v1/document-type", request, ct);

    public async Task<UmbracoResponse<object>> DeleteDocumentTypeAsync(Guid id, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/document-type/{id}", ct);

    // ── Data Types ───────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<DataTypeResponse>>> GetDataTypesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<DataTypeResponse>>($"umbraco/management/api/v1/data-type?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<DataTypeResponse>> GetDataTypeByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<DataTypeResponse>($"umbraco/management/api/v1/data-type/{id}", ct);

    // ── Languages ────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<IEnumerable<LanguageResponse>>> GetLanguagesAsync(CancellationToken ct = default) =>
        await GetAsync<IEnumerable<LanguageResponse>>("umbraco/management/api/v1/language", ct);

    public async Task<UmbracoResponse<LanguageResponse>> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateLanguageRequest, LanguageResponse>("umbraco/management/api/v1/language", request, ct);

    public async Task<UmbracoResponse<object>> DeleteLanguageAsync(string isoCode, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/language/{isoCode}", ct);

    // ── Templates ────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<TemplateResponse>>> GetTemplatesAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<TemplateResponse>>($"umbraco/management/api/v1/template?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<TemplateResponse>> GetTemplateByAliasAsync(string alias, CancellationToken ct = default) =>
        await GetAsync<TemplateResponse>($"umbraco/management/api/v1/template?alias={Uri.EscapeDataString(alias)}", ct);

    // ── Members ──────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var url = $"umbraco/management/api/v1/member?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(group)) url += $"&memberGroupName={Uri.EscapeDataString(group)}";
        return await GetAsync<PagedResponse<MemberResponse>>(url, ct);
    }

    public async Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<MemberResponse>($"umbraco/management/api/v1/member/{id}", ct);

    public async Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(CreateMemberRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateMemberRequest, MemberResponse>("umbraco/management/api/v1/member", request, ct);

    public async Task<UmbracoResponse<object>> DeleteMemberAsync(Guid id, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/member/{id}", ct);

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<UserResponse>>($"umbraco/management/api/v1/user?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<UserResponse>($"umbraco/management/api/v1/user/{id}", ct);

    public async Task<UmbracoResponse<object>> InviteUserAsync(InviteUserRequest request, CancellationToken ct = default) =>
        await PostAsync<InviteUserRequest, object>("umbraco/management/api/v1/user/invite", request, ct);

    // ── Dictionary ───────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<DictionaryItemResponse>>> GetDictionaryItemsAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<DictionaryItemResponse>>($"umbraco/management/api/v1/dictionary?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<DictionaryItemResponse>> GetDictionaryItemByKeyAsync(string key, CancellationToken ct = default) =>
        await GetAsync<DictionaryItemResponse>($"umbraco/management/api/v1/dictionary/{Uri.EscapeDataString(key)}", ct);

    public async Task<UmbracoResponse<DictionaryItemResponse>> CreateDictionaryItemAsync(CreateDictionaryItemRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateDictionaryItemRequest, DictionaryItemResponse>("umbraco/management/api/v1/dictionary", request, ct);

    // ── Webhooks ──────────────────────────────────────────────────────────────

    public async Task<UmbracoResponse<PagedResponse<WebhookResponse>>> GetWebhooksAsync(
        int skip = 0, int take = 20, CancellationToken ct = default) =>
        await GetAsync<PagedResponse<WebhookResponse>>($"umbraco/management/api/v1/webhook?skip={skip}&take={take}", ct);

    public async Task<UmbracoResponse<WebhookResponse>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken ct = default) =>
        await PostAsync<CreateWebhookRequest, WebhookResponse>("umbraco/management/api/v1/webhook", request, ct);

    public async Task<UmbracoResponse<object>> DeleteWebhookAsync(Guid id, CancellationToken ct = default) =>
        await DeleteAsync($"umbraco/management/api/v1/webhook/{id}", ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<UmbracoResponse<TResponse>> GetAsync<TResponse>(string url, CancellationToken ct)
        => GuardedAsync(async () =>
        {
            var response = await _http.GetAsync(url, ct);
            return await DeserializeAsync<TResponse>(response, ct);
        }, ct);

    private Task<UmbracoResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string url, TRequest body, CancellationToken ct)
        => GuardedAsync(async () =>
        {
            var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
            return await DeserializeAsync<TResponse>(response, ct);
        }, ct);

    private Task<UmbracoResponse<TResponse>> PutAsync<TRequest, TResponse>(
        string url, TRequest body, CancellationToken ct)
        => GuardedAsync(async () =>
        {
            var response = await _http.PutAsJsonAsync(url, body, JsonOptions, ct);
            return await DeserializeAsync<TResponse>(response, ct);
        }, ct);

    private Task<UmbracoResponse<object>> DeleteAsync(string url, CancellationToken ct)
        => GuardedAsync(async () =>
        {
            var response = await _http.DeleteAsync(url, ct);
            return response.IsSuccessStatusCode
                ? UmbracoResponse<object>.Success(new object(), (int)response.StatusCode)
                : await BuildErrorAsync<object>(response, ct);
        }, ct);

    private Task<UmbracoResponse<TResponse>> SendAsync<TResponse>(
        HttpMethod method, string url, HttpContent content, CancellationToken ct)
        => GuardedAsync(async () =>
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            var response = await _http.SendAsync(request, ct);
            return await DeserializeAsync<TResponse>(response, ct);
        }, ct);

    /// <summary>
    /// Converts transport-level failures (host unreachable, timeout, unreadable body)
    /// into a failed <see cref="UmbracoResponse{T}"/> with status code 0, so callers
    /// see a normal failure instead of an exception. A genuine cancellation requested
    /// via <paramref name="ct"/> is left to propagate.
    /// </summary>
    private static async Task<UmbracoResponse<T>> GuardedAsync<T>(
        Func<Task<UmbracoResponse<T>>> action, CancellationToken ct)
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
            return UmbracoResponse<T>.Failure(0, $"Could not reach the Umbraco instance: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return UmbracoResponse<T>.Failure(0, $"The Umbraco instance returned an unreadable response: {ex.Message}");
        }
    }

    private static async Task<UmbracoResponse<TResponse>> DeserializeAsync<TResponse>(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return await BuildErrorAsync<TResponse>(response, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return UmbracoResponse<TResponse>.Success(default!, (int)response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return data is not null
            ? UmbracoResponse<TResponse>.Success(data, (int)response.StatusCode)
            : UmbracoResponse<TResponse>.Failure((int)response.StatusCode, "Empty response body");
    }

    private static async Task<UmbracoResponse<T>> BuildErrorAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        string message;
        try
        {
            var err = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
            message = err.TryGetProperty("title", out var title) ? title.GetString() ?? body
                    : err.TryGetProperty("detail", out var detail) ? detail.GetString() ?? body
                    : body;
        }
        catch
        {
            message = body;
        }
        return UmbracoResponse<T>.Failure((int)response.StatusCode, message);
    }
}
