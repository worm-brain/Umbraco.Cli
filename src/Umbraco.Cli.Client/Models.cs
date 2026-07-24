using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// ── Envelope ─────────────────────────────────────────────────────────────────

public record UmbracoResponse<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static UmbracoResponse<T> Success(T data, int code = 200) =>
        new()
        {
            IsSuccess = true,
            Data = data,
            StatusCode = code,
        };

    public static UmbracoResponse<T> Failure(int code, string message) =>
        new()
        {
            IsSuccess = false,
            StatusCode = code,
            ErrorMessage = message,
        };
}

/// <summary>
/// The unit payload for operations that succeed without returning data (delete,
/// publish, invite, …). Used as <c>UmbracoResponse&lt;Empty&gt;</c> so the "no payload"
/// intent is explicit in the type, replacing a meaningless <c>object</c> placeholder.
/// </summary>
public sealed record Empty
{
    public static readonly Empty Value = new();
}

public record PagedResponse<T>
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("items")]
    public IEnumerable<T> Items { get; init; } = [];
}

// ── Auth ─────────────────────────────────────────────────────────────────────

public record TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";
}

public record CurrentUserResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = "";
}

// ── Content ──────────────────────────────────────────────────────────────────

public record ContentItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("contentType")]
    public ContentTypeReference? ContentType { get; init; }

    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }

    [JsonPropertyName("urls")]
    public IEnumerable<UrlInfo>? Urls { get; init; }

    [JsonPropertyName("isPublished")]
    public bool IsPublished { get; init; }

    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }

    [JsonPropertyName("updateDate")]
    public DateTimeOffset UpdateDate { get; init; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?>? Properties { get; init; }
}

public record ContentTypeReference
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";
}

public record ContentParentReference
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
}

public record UrlInfo
{
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}

public record CreateContentRequest
{
    [JsonPropertyName("contentType")]
    public required ContentTypeReference ContentType { get; init; }

    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }

    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];

    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariant> Variants { get; init; } = [];
}

public record UpdateContentRequest
{
    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];

    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariant> Variants { get; init; } = [];
}

public record ContentValue
{
    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("value")]
    public object? Value { get; init; }

    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("segment")]
    public string? Segment { get; init; }
}

public record ContentVariant
{
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("segment")]
    public string? Segment { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

public record PublishContentRequest
{
    [JsonPropertyName("publishSchedules")]
    public IEnumerable<PublishSchedule> PublishSchedules { get; init; } = [];
}

public record PublishSchedule
{
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }
}

// ── Media ────────────────────────────────────────────────────────────────────

public record MediaItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("mediaType")]
    public ContentTypeReference? MediaType { get; init; }

    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }

    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }

    [JsonPropertyName("updateDate")]
    public DateTimeOffset UpdateDate { get; init; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?>? Properties { get; init; }
}

// ── Document Types ───────────────────────────────────────────────────────────

public record DocumentTypeResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isElement")]
    public bool IsElement { get; init; }

    [JsonPropertyName("allowedAsRoot")]
    public bool AllowedAsRoot { get; init; }
}

/// <summary>
/// Create payload for a document type. The Umbraco 17 API requires the full set of
/// fields listed here (issue #47 — omitting <c>icon</c>, <c>cleanup</c>, the varies-by
/// flags or the allowed-* collections 400s the request). Collections default to empty
/// and the flags to false so a minimal create (name + alias) succeeds.
/// </summary>
public record CreateDocumentTypeRequest
{
    /// <summary>
    /// Caller-supplied id. Umbraco accepts a client-generated GUID here, which lets the
    /// CLI return the new id even though creates respond with an empty body (issue #43).
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    /// <summary>Backoffice icon. Required by the API; defaults to a generic document icon (issue #47).</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "icon-document";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isElement")]
    public bool IsElement { get; init; }

    [JsonPropertyName("allowedAsRoot")]
    public bool AllowedAsRoot { get; init; }

    /// <summary>Whether the type varies by culture. Required flag; defaults to false.</summary>
    [JsonPropertyName("variesByCulture")]
    public bool VariesByCulture { get; init; }

    /// <summary>Whether the type varies by segment. Required flag; defaults to false.</summary>
    [JsonPropertyName("variesBySegment")]
    public bool VariesBySegment { get; init; }

    /// <summary>Version-cleanup policy. Required object; defaults to "no cleanup".</summary>
    [JsonPropertyName("cleanup")]
    public DocumentTypeCleanup Cleanup { get; init; } = new();

    [JsonPropertyName("containers")]
    public IEnumerable<object> Containers { get; init; } = [];

    [JsonPropertyName("properties")]
    public IEnumerable<object> Properties { get; init; } = [];

    /// <summary>Allowed child document types. Required collection; defaults to empty.</summary>
    [JsonPropertyName("allowedDocumentTypes")]
    public IEnumerable<object> AllowedDocumentTypes { get; init; } = [];

    /// <summary>Compositions this type inherits from. Required collection; defaults to empty.</summary>
    [JsonPropertyName("compositions")]
    public IEnumerable<object> Compositions { get; init; } = [];

    /// <summary>Templates allowed on this type. Required collection; defaults to empty.</summary>
    [JsonPropertyName("allowedTemplates")]
    public IEnumerable<ReferenceById> AllowedTemplates { get; init; } = [];
}

/// <summary>
/// Document-type version cleanup policy. Defaults to "prevent cleanup" so a minimal
/// create does not silently opt content into version pruning.
/// </summary>
public record DocumentTypeCleanup
{
    [JsonPropertyName("preventCleanup")]
    public bool PreventCleanup { get; init; } = true;

    [JsonPropertyName("keepAllVersionsNewerThanDays")]
    public int? KeepAllVersionsNewerThanDays { get; init; }

    [JsonPropertyName("keepLatestVersionPerDayForDays")]
    public int? KeepLatestVersionPerDayForDays { get; init; }
}

// ── Data Types ───────────────────────────────────────────────────────────────

public record DataTypeResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("editorAlias")]
    public string EditorAlias { get; init; } = "";

    [JsonPropertyName("editorUiAlias")]
    public string? EditorUiAlias { get; init; }
}

// ── Languages ────────────────────────────────────────────────────────────────

public record LanguageResponse
{
    [JsonPropertyName("isoCode")]
    public string IsoCode { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("isMandatory")]
    public bool IsMandatory { get; init; }

    [JsonPropertyName("fallbackIsoCode")]
    public string? FallbackIsoCode { get; init; }
}

public record CreateLanguageRequest
{
    [JsonPropertyName("isoCode")]
    public required string IsoCode { get; init; }

    /// <summary>
    /// Human-readable language name. Required by the API (issue #47) — without it the
    /// create 400s with "The Name field is required.". Defaults are derived from the
    /// culture by the command when the caller does not supply one.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("isMandatory")]
    public bool IsMandatory { get; init; }

    [JsonPropertyName("fallbackIsoCode")]
    public string? FallbackIsoCode { get; init; }
}

// ── Templates ────────────────────────────────────────────────────────────────

public record TemplateResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("masterTemplate")]
    public ContentTypeReference? MasterTemplate { get; init; }
}

// ── Members ──────────────────────────────────────────────────────────────────

public record MemberResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("memberType")]
    public ContentTypeReference? MemberType { get; init; }

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; init; }

    [JsonPropertyName("isLockedOut")]
    public bool IsLockedOut { get; init; }

    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }
}

public record CreateMemberRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("memberType")]
    public required ContentTypeReference MemberType { get; init; }

    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; init; } = true;

    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];
}

// ── Users ─────────────────────────────────────────────────────────────────────

public record UserResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = "";

    [JsonPropertyName("state")]
    public string State { get; init; } = "";

    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }
}

public record InviteUserRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("userGroupIds")]
    public IEnumerable<ReferenceById> UserGroupIds { get; init; } = [];
}

public record ReferenceById
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
}

// ── Dictionary ───────────────────────────────────────────────────────────────

public record DictionaryItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("translations")]
    public IEnumerable<DictionaryTranslation>? Translations { get; init; }
}

public record DictionaryTranslation
{
    [JsonPropertyName("isoCode")]
    public string IsoCode { get; init; } = "";

    [JsonPropertyName("translation")]
    public string Translation { get; init; } = "";
}

public record CreateDictionaryItemRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("translations")]
    public IEnumerable<DictionaryTranslation> Translations { get; init; } = [];
}

// ── Webhooks ──────────────────────────────────────────────────────────────────

public record WebhookResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("events")]
    public IEnumerable<string>? Events { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

public record CreateWebhookRequest
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("events")]
    public IEnumerable<string> Events { get; init; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; init; } = [];

    [JsonPropertyName("contentTypeKeys")]
    public IEnumerable<Guid> ContentTypeKeys { get; init; } = [];
}
