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
    /// <summary>
    /// Optional client-supplied id for an idempotent create (#86/#140). Umbraco 14+ honours a
    /// client-supplied document id, so re-running a provisioning script with the same id does not
    /// create a duplicate. When null the client generates a new id.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

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

/// <summary>
/// A single version of a document (issue #58). Returned when listing a document's version
/// history; a version's <see cref="Id"/> is what <c>content rollback</c> targets.
/// </summary>
public record DocumentVersionResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("versionDate")]
    public DateTimeOffset VersionDate { get; init; }

    [JsonPropertyName("isCurrentDraftVersion")]
    public bool IsCurrentDraftVersion { get; init; }

    [JsonPropertyName("isCurrentPublishedVersion")]
    public bool IsCurrentPublishedVersion { get; init; }

    [JsonPropertyName("preventCleanup")]
    public bool PreventCleanup { get; init; }
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

/// <summary>
/// Create payload for a media item (issue #57). Umbraco 14+ creates media as JSON that
/// references a previously-staged upload by <c>temporaryFileId</c> (see the two-step upload
/// flow in <c>UploadMediaAsync</c>), rather than posting the file bytes to <c>/media</c>
/// directly. The id is client-generated so the new item's id can be surfaced from the empty
/// 201 body (issue #43/#74).
/// </summary>
public record CreateMediaRequest
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonPropertyName("mediaType")]
    public required ReferenceById MediaType { get; init; }

    [JsonPropertyName("parent")]
    public ReferenceById? Parent { get; init; }

    [JsonPropertyName("variants")]
    public IEnumerable<MediaVariant> Variants { get; init; } = [];

    [JsonPropertyName("values")]
    public IEnumerable<MediaValue> Values { get; init; } = [];
}

/// <summary>A media variant (name per culture/segment). Media are invariant by default (null culture).</summary>
public record MediaVariant
{
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("segment")]
    public string? Segment { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>A single media property value (e.g. <c>umbracoFile</c> pointing at a staged temporary file).</summary>
public record MediaValue
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

/// <summary>A minimal id+name entity, used to resolve a media type by name from the item search endpoint.</summary>
public record NamedEntity
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

// ── Media Types ──────────────────────────────────────────────────────────────

/// <summary>
/// Command-facing view of a media type. Populated fully from a single-item GET; the
/// list view (backed by the media-type tree) only carries id/name/icon, since tree
/// items do not expose alias/description (mirrors the document-type shape).
/// </summary>
public record MediaTypeResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("isElement")]
    public bool IsElement { get; init; }

    [JsonPropertyName("allowedAsRoot")]
    public bool AllowedAsRoot { get; init; }
}

/// <summary>
/// Create payload for a media type. Like the document-type create (issue #47), the
/// Umbraco 17 API requires a full field set; the client fills the defaults (icon, the
/// varies-by flags, the allowed-* / composition / container / property collections) so a
/// minimal create needs only a name and alias.
/// </summary>
public record CreateMediaTypeRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    /// <summary>Backoffice icon. Required by the API; defaults to a generic image icon.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "icon-picture";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isElement")]
    public bool IsElement { get; init; }

    [JsonPropertyName("allowedAsRoot")]
    public bool AllowedAsRoot { get; init; }
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

/// <summary>
/// Create payload for a data type (issue #59). A data type wraps a property editor:
/// <c>editorAlias</c> is the backend editor (e.g. <c>Umbraco.TextBox</c>) and
/// <c>editorUiAlias</c> the backoffice UI (e.g. <c>Umb.PropertyEditorUi.TextBox</c>).
/// Editor configuration values default to empty.
/// </summary>
public record CreateDataTypeRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("editorAlias")]
    public required string EditorAlias { get; init; }

    [JsonPropertyName("editorUiAlias")]
    public required string EditorUiAlias { get; init; }
}

/// <summary>
/// Update payload for a data type (issue #59). The underlying PUT is a full replace, so null
/// fields are preserved by reading the current data type and merging; its editor configuration
/// <c>values</c> are always preserved (never exposed here, so an update cannot wipe them).
/// </summary>
public record UpdateDataTypeRequest
{
    /// <summary>New name, or null to keep the current one.</summary>
    public string? Name { get; init; }

    /// <summary>New backend editor alias, or null to keep the current one.</summary>
    public string? EditorAlias { get; init; }

    /// <summary>New backoffice editor UI alias, or null to keep the current one.</summary>
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

/// <summary>
/// Update payload for a language (issue #59). The language is keyed by ISO code in the URL.
/// The underlying PUT is a full replace, so each null field is preserved by reading the
/// current language and merging (avoids silently clearing the default/mandatory flags or the
/// fallback culture when only the name is being changed).
/// </summary>
public record UpdateLanguageRequest
{
    /// <summary>New name, or null to keep the current one.</summary>
    public string? Name { get; init; }

    /// <summary>New default state, or null to keep the current one.</summary>
    public bool? IsDefault { get; init; }

    /// <summary>New mandatory state, or null to keep the current one.</summary>
    public bool? IsMandatory { get; init; }

    /// <summary>New fallback ISO code, or null to keep the current one.</summary>
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

/// <summary>Create payload for a template (issue #59). Content is the Razor view body.</summary>
public record CreateTemplateRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

/// <summary>
/// Update payload for a template (issue #59). The underlying PUT is a full replace, so each
/// null field is preserved by reading the current template and merging (avoids silently
/// blanking the Razor <c>content</c> when only the name is being changed).
/// </summary>
public record UpdateTemplateRequest
{
    /// <summary>New name, or null to keep the current one.</summary>
    public string? Name { get; init; }

    /// <summary>New alias, or null to keep the current one.</summary>
    public string? Alias { get; init; }

    /// <summary>New Razor content, or null to keep the current one.</summary>
    public string? Content { get; init; }
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
    /// <summary>
    /// Caller-supplied id for an idempotent create (#86). Serialized so Umbraco uses it; a GUID
    /// is generated by the command when null. Omitted from the payload when null.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

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

/// <summary>
/// Partial-update payload for a member (issue #59). Only the set fields are changed; the
/// client reads the current member and merges these over it before the PUT (which is a full
/// replace) so unspecified fields — groups, property values, password — are preserved.
/// </summary>
public record UpdateMemberRequest
{
    /// <summary>New email, or null to keep the current one.</summary>
    public string? Email { get; init; }

    /// <summary>New display name, or null to keep the current one.</summary>
    public string? Name { get; init; }

    /// <summary>New approved state, or null to keep the current one.</summary>
    public bool? IsApproved { get; init; }
}

// ── Member Types ─────────────────────────────────────────────────────────────

/// <summary>
/// Command-facing view of a member type (issue #56). Populated fully from a single-item
/// GET; the list view (backed by the member-type tree) only carries id/name/icon.
/// </summary>
public record MemberTypeResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}

/// <summary>
/// Create payload for a member type (issue #56). As with document/media types (issue #47),
/// the API requires a full field set; the client fills the defaults (icon, the varies-by
/// flags, the composition/container/property collections) so a minimal create needs only a
/// name and alias.
/// </summary>
public record CreateMemberTypeRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    /// <summary>Backoffice icon. Required by the API; defaults to a generic member icon.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "icon-user";

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Update payload for a member type (issue #56). The underlying PUT is a full replace, so this
/// exposes only the scalar fields the CLI understands (name/alias/description/icon); everything
/// else on the type - its properties, containers, compositions and varies-by flags - is
/// preserved by reading the current type as raw JSON and patching only the supplied fields
/// before writing it back. A null field keeps the current value.
/// </summary>
public record UpdateMemberTypeRequest
{
    /// <summary>New name, or null to keep the current one.</summary>
    public string? Name { get; init; }

    /// <summary>New alias, or null to keep the current one.</summary>
    public string? Alias { get; init; }

    /// <summary>New description, or null to keep the current one.</summary>
    public string? Description { get; init; }

    /// <summary>New backoffice icon, or null to keep the current one.</summary>
    public string? Icon { get; init; }
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
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

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

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    /// <summary>
    /// Subscribed events. The API returns these as objects (issue #46 —
    /// <c>{eventName, eventType, alias}</c>), not bare strings, so deserializing to
    /// <c>string[]</c> threw. Modelled as <see cref="WebhookEvent"/>.
    /// </summary>
    [JsonPropertyName("events")]
    public IEnumerable<WebhookEvent>? Events { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>Document/media/member type keys this webhook is scoped to (empty = all).</summary>
    [JsonPropertyName("contentTypeKeys")]
    public IEnumerable<Guid>? ContentTypeKeys { get; init; }

    /// <summary>Custom HTTP headers sent with the webhook request.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>A single event a webhook is subscribed to, as returned by the API (issue #46).</summary>
public record WebhookEvent
{
    [JsonPropertyName("eventName")]
    public string EventName { get; init; } = "";

    [JsonPropertyName("eventType")]
    public string? EventType { get; init; }

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }
}

public record CreateWebhookRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    /// <summary>Optional human-readable name for the webhook (#80).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Optional description for the webhook (#80).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

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
