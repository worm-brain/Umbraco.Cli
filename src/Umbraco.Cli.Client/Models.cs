using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// ── Envelope ─────────────────────────────────────────────────────────────────

/// <summary>
/// Why a client call failed, classified by <em>where</em> the failure was caught rather than
/// guessed from the HTTP status (#152). The status code alone is a poor fault signal - a 400 can
/// be the CLI's fault (a malformed request, e.g. #149) and a 500 can be the server's fault
/// (e.g. #150) - so this gives a caller a machine-readable answer to "whose problem is this?".
/// </summary>
public enum FailureCategory
{
    /// <summary>Not a failure (a success response).</summary>
    None = 0,

    /// <summary>The Umbraco instance could not be reached (DNS/connection/TLS); no HTTP response.</summary>
    Unreachable,

    /// <summary>The request to the Umbraco instance timed out before a response.</summary>
    Timeout,

    /// <summary>The server responded and rejected the request (a 4xx). Often bad input or the CLI's request.</summary>
    RequestRejected,

    /// <summary>The server responded with an internal error (a 5xx, or an undeclared status). A server-side problem.</summary>
    ServerError,

    /// <summary>
    /// The server responded but the body did not match what this CLI expected - a likely sign of
    /// an Umbraco version the generated client was not built against. Reserved here; populated by #154.
    /// </summary>
    UnexpectedResponse,

    /// <summary>
    /// The command line itself was invalid (an unparseable value, an unknown option, a missing
    /// argument), so no request was sent (#203). Always the caller's to fix.
    /// </summary>
    InvalidArgument,
}

/// <summary>Wire-name mapping for <see cref="FailureCategory"/> (the string emitted in the JSON error envelope).</summary>
public static class FailureCategoryExtensions
{
    /// <summary>
    /// The snake_case wire name for a category, or null for <see cref="FailureCategory.None"/>
    /// (so a success or an uncategorised failure emits no <c>category</c> field).
    /// </summary>
    /// <param name="category">The category to convert.</param>
    /// <returns>The wire string, or null when there is no category to emit.</returns>
    public static string? ToWire(this FailureCategory category) =>
        category switch
        {
            FailureCategory.Unreachable => "unreachable",
            FailureCategory.Timeout => "timeout",
            FailureCategory.RequestRejected => "request_rejected",
            FailureCategory.ServerError => "server_error",
            FailureCategory.UnexpectedResponse => "unexpected_response",
            FailureCategory.InvalidArgument => "invalid_argument",
            _ => null,
        };
}

public record UmbracoResponse<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Why the call failed (#152); <see cref="FailureCategory.None"/> on success.</summary>
    public FailureCategory Category { get; init; }

    public static UmbracoResponse<T> Success(T data, int code = 200) =>
        new()
        {
            IsSuccess = true,
            Data = data,
            StatusCode = code,
            Category = FailureCategory.None,
        };

    /// <summary>
    /// Builds a failed response. When <paramref name="category"/> is null it is derived from the
    /// status code (a reasonable default for direct callers); the request guard passes the precise
    /// category explicitly, because a status of 0 alone cannot tell an unreachable host from a
    /// timeout.
    /// </summary>
    /// <param name="code">The HTTP status code (0 when no response was received).</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="category">The explicit failure category, or null to derive one from <paramref name="code"/>.</param>
    /// <returns>A failed <see cref="UmbracoResponse{T}"/>.</returns>
    public static UmbracoResponse<T> Failure(
        int code,
        string message,
        FailureCategory? category = null
    ) =>
        new()
        {
            IsSuccess = false,
            StatusCode = code,
            ErrorMessage = message,
            Category = category ?? DeriveCategory(code),
        };

    /// <summary>
    /// Re-wraps someone else's failure as this payload type, carrying the status, message and
    /// category across unchanged. A command that has to read something before it can write - to
    /// resolve an alias, say - must surface the read's own failure rather than inventing one, and
    /// copying three fields by hand at each site is how one of them ends up dropping the category
    /// and downgrading a "host unreachable" to a generic error.
    /// </summary>
    /// <typeparam name="TOther">The payload type of the failed response.</typeparam>
    /// <param name="failed">The failed response to re-wrap.</param>
    /// <returns>The same failure, typed as <see cref="UmbracoResponse{T}"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="failed"/> is not a failure.</exception>
    public static UmbracoResponse<T> FailureFrom<TOther>(UmbracoResponse<TOther> failed) =>
        failed.IsSuccess
            ? throw new ArgumentException(
                "Only a failed response can be re-wrapped as a failure.",
                nameof(failed)
            )
            : new()
            {
                IsSuccess = false,
                StatusCode = failed.StatusCode,
                ErrorMessage = failed.ErrorMessage,
                Category = failed.Category,
            };

    /// <summary>
    /// Best-effort category from a status code, for callers that do not pass one explicitly.
    /// A status of 0 (no response) defaults to <see cref="FailureCategory.Unreachable"/>; the
    /// timeout path sets <see cref="FailureCategory.Timeout"/> itself.
    /// </summary>
    /// <param name="code">The HTTP status code.</param>
    /// <returns>The derived category.</returns>
    private static FailureCategory DeriveCategory(int code) =>
        code switch
        {
            0 => FailureCategory.Unreachable,
            >= 500 => FailureCategory.ServerError,
            >= 400 => FailureCategory.RequestRejected,
            _ => FailureCategory.ServerError,
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
    public ContentTypeRef? ContentType { get; init; }

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

    /// <summary>
    /// The item's property values (#168). Null when the item was read from a list or tree walk,
    /// which does not carry them; an empty array means the item genuinely has none.
    /// </summary>
    [JsonPropertyName("values")]
    public IEnumerable<ContentValueResponse>? Values { get; init; }

    /// <summary>
    /// Every variant, not just the first (#168). <see cref="Name"/> and the dates stay flattened
    /// from the first variant for compatibility; this is where a multilingual item's other
    /// cultures, and each culture's publication state, actually appear.
    /// </summary>
    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariantResponse>? Variants { get; init; }

    /// <summary>The item's template (#168/#178). Null when it has none.</summary>
    [JsonPropertyName("template")]
    public ContentTemplateReference? Template { get; init; }
}

/// <summary>A property value as read back from a content or media item (#168/#172).</summary>
public record ContentValueResponse
{
    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("segment")]
    public string? Segment { get; init; }

    /// <summary>
    /// The backend editor behind this property, e.g. <c>Umbraco.MediaPicker3</c>. Present on a
    /// read but not accepted on a write - it is what decides the value's shape (#174).
    /// </summary>
    [JsonPropertyName("editorAlias")]
    public string? EditorAlias { get; init; }

    /// <summary>The value, in whatever shape the editor uses.</summary>
    [JsonPropertyName("value")]
    public JsonNode? Value { get; init; }
}

/// <summary>One variant (culture/segment) of a content item, as read back (#168).</summary>
public record ContentVariantResponse
{
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    [JsonPropertyName("segment")]
    public string? Segment { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Publication state for this culture, e.g. <c>Published</c>, <c>Draft</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("createDate")]
    public DateTimeOffset? CreateDate { get; init; }

    [JsonPropertyName("updateDate")]
    public DateTimeOffset? UpdateDate { get; init; }

    [JsonPropertyName("publishDate")]
    public DateTimeOffset? PublishDate { get; init; }
}

/// <summary>A document's culture-and-hostname bindings (#180).</summary>
public record DomainsResponse
{
    /// <summary>The culture served when no domain matches, or null for none.</summary>
    [JsonPropertyName("defaultIsoCode")]
    public string? DefaultIsoCode { get; init; }

    [JsonPropertyName("domains")]
    public IEnumerable<DomainBinding> Domains { get; init; } = [];
}

/// <summary>One hostname bound to one culture (#180).</summary>
public record DomainBinding
{
    /// <summary>The hostname, optionally with a path, e.g. <c>example.com/da</c>.</summary>
    [JsonPropertyName("domainName")]
    public string DomainName { get; init; } = "";

    [JsonPropertyName("isoCode")]
    public string IsoCode { get; init; } = "";
}

/// <summary>
/// The complete set of domains for a document (#180). The API's PUT replaces, so this is not a
/// patch - `content domains set` reads the current set first so a caller can add one without
/// restating the rest.
/// </summary>
public record SetDomainsRequest
{
    [JsonPropertyName("defaultIsoCode")]
    public string? DefaultIsoCode { get; init; }

    [JsonPropertyName("domains")]
    public IEnumerable<DomainBinding> Domains { get; init; } = [];
}

/// <summary>
/// A type reference on the <b>write</b> side: the caller supplies either an id or an alias, and
/// the client resolves whichever is missing. Distinct from <see cref="ContentTypeRef"/>, which is
/// what a read returns - there the alias may be genuinely unknown, here it may not.
/// </summary>
public record ContentTypeReference
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";
}

/// <summary>
/// A type reference as a <b>read</b> returns it (#163). The Management API's reference carries
/// only an id, so the alias is looked up; it is null rather than empty when that fails, so the
/// envelope omits it instead of showing a field that looks populated and is not.
/// </summary>
public record ContentTypeRef
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }
}

public record ContentParentReference
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
}

/// <summary>
/// A flat node of a content or media tree walk (issue #89). The <see cref="Depth"/> and
/// <see cref="ParentId"/> make the hierarchy reconstructable from a flat, agent-friendly list, so a
/// caller never has to nest the output to understand placement.
/// </summary>
public record TreeItem
{
    /// <summary>The node id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The parent id, or null when the node sits at the walked root.</summary>
    [JsonPropertyName("parentId")]
    public Guid? ParentId { get; init; }

    /// <summary>Depth below the walked root: 1 for the first listed level, 2 for its children, and so on.</summary>
    [JsonPropertyName("depth")]
    public int Depth { get; init; }

    /// <summary>Whether the node has children (which may be beyond the requested depth).</summary>
    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; init; }
}

/// <summary>
/// The outcome of a publish-with-descendants request (issue #90). The server runs the branch
/// publish as a background task; <see cref="TaskId"/> identifies it and <see cref="IsComplete"/>
/// says whether it has finished (immediately, or after <c>--wait</c> polling).
/// </summary>
public record PublishDescendantsResult
{
    /// <summary>The background task id the server assigned, or null if it completed synchronously.</summary>
    [JsonPropertyName("taskId")]
    public Guid? TaskId { get; init; }

    /// <summary>Whether the branch publish has completed.</summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; init; }
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

    /// <summary>
    /// The template to set on the new document (#162). Null means "use the document type's
    /// default": Umbraco 17 requires the <c>template</c> key to be present on a create body and
    /// treats an explicit null as the default, which is why it is always serialized (#134).
    /// </summary>
    [JsonPropertyName("template")]
    public ContentTemplateReference? Template { get; init; }
}

public record UpdateContentRequest
{
    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];

    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariant> Variants { get; init; } = [];

    /// <summary>
    /// The template to set on the document (#162). Null leaves the document's current template
    /// alone - it is <b>not</b> a request to remove it, because omitting the field is how most
    /// bodies are written and clearing a template makes the page 404 (#178).
    /// </summary>
    [JsonPropertyName("template")]
    public ContentTemplateReference? Template { get; init; }
}

/// <summary>
/// A reference to a template, by id or by alias (#162). Exactly one is needed; when both are
/// given the id wins, because it needs no lookup.
/// </summary>
public record ContentTemplateReference
{
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }
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
    public ContentTypeRef? MediaType { get; init; }

    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }

    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }

    [JsonPropertyName("updateDate")]
    public DateTimeOffset UpdateDate { get; init; }

    /// <summary>
    /// The item's property values (#172). This is where the file actually lives: Umbraco keeps
    /// <c>umbracoFile</c>, <c>umbracoWidth</c>, <c>umbracoHeight</c>, <c>umbracoBytes</c> and
    /// <c>umbracoExtension</c> here, so dropping it hid every piece of file metadata.
    /// </summary>
    [JsonPropertyName("values")]
    public IEnumerable<ContentValueResponse>? Values { get; init; }

    /// <summary>
    /// The item's public URLs, one per culture (#172). Read from a separate endpoint, so null
    /// when it could not be reached rather than an empty list.
    /// </summary>
    [JsonPropertyName("urls")]
    public IEnumerable<UrlInfo>? Urls { get; init; }
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

    /// <summary>The type's icon. Dropped before #160, though the media-type record always had it.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>Whether documents of this type vary by culture (#161's read half).</summary>
    [JsonPropertyName("variesByCulture")]
    public bool VariesByCulture { get; init; }

    [JsonPropertyName("variesBySegment")]
    public bool VariesBySegment { get; init; }

    /// <summary>
    /// The type's properties (#160). The help text claimed these were returned for three
    /// releases while they were being dropped in the mapping.
    /// </summary>
    [JsonPropertyName("properties")]
    public IEnumerable<DocumentTypePropertyResponse>? Properties { get; init; }

    /// <summary>The property groups/tabs the properties sit in (#160).</summary>
    [JsonPropertyName("containers")]
    public IEnumerable<DocumentTypeContainerResponse>? Containers { get; init; }

    /// <summary>Types this one composes (#160).</summary>
    [JsonPropertyName("compositions")]
    public IEnumerable<Guid>? Compositions { get; init; }

    /// <summary>Templates allowed on documents of this type (#160/#162).</summary>
    [JsonPropertyName("allowedTemplates")]
    public IEnumerable<Guid>? AllowedTemplates { get; init; }

    /// <summary>The template applied when none is chosen (#162).</summary>
    [JsonPropertyName("defaultTemplate")]
    public Guid? DefaultTemplate { get; init; }
}

/// <summary>One property on a document type (#160).</summary>
public record DocumentTypePropertyResponse
{
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The data type behind this property - what decides its value shape (#174).</summary>
    [JsonPropertyName("dataType")]
    public Guid? DataType { get; init; }

    /// <summary>The group/tab this property belongs to.</summary>
    [JsonPropertyName("container")]
    public Guid? Container { get; init; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; init; }

    [JsonPropertyName("variesByCulture")]
    public bool VariesByCulture { get; init; }

    [JsonPropertyName("variesBySegment")]
    public bool VariesBySegment { get; init; }
}

/// <summary>A property group or tab on a document type (#160).</summary>
public record DocumentTypeContainerResponse
{
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Whether this is a tab or a group.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; init; }
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

    /// <summary>
    /// The backend property editor, e.g. <c>Umbraco.TextBox</c>. Null when it could not be read
    /// (#176) - distinct from a type that genuinely has none, which the empty string would blur.
    /// </summary>
    [JsonPropertyName("editorAlias")]
    public string? EditorAlias { get; init; }

    [JsonPropertyName("editorUiAlias")]
    public string? EditorUiAlias { get; init; }

    /// <summary>
    /// The editor's configuration (#170): a dropdown's items, a picker's filters, an RTE's
    /// toolbar. The write path already reads and merges these so an update cannot wipe them - this
    /// simply stops the read from hiding them.
    /// </summary>
    [JsonPropertyName("values")]
    public IEnumerable<DataTypeValueResponse>? Values { get; init; }
}

/// <summary>One configuration entry on a data type (#170).</summary>
public record DataTypeValueResponse
{
    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    [JsonPropertyName("value")]
    public JsonNode? Value { get; init; }
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
    public ContentTypeRef? MemberType { get; init; }

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; init; }

    /// <summary>The login name. Often the email, but they are separate fields (#185).</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>
    /// The groups this member belongs to, by id (#185). Member groups are referenced by id, not
    /// name - worth knowing when filtering with <c>members list --group</c>, which takes a name.
    /// </summary>
    [JsonPropertyName("groups")]
    public IEnumerable<Guid>? Groups { get; init; }

    /// <summary>The member's custom property values (#185), in the same shape as content.</summary>
    [JsonPropertyName("values")]
    public IEnumerable<ContentValueResponse>? Values { get; init; }

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

    /// <summary>New login name, or null to keep the current one (#185).</summary>
    public string? Username { get; init; }

    /// <summary>
    /// Group ids to set, replacing the member's current groups. Null keeps them - the read-merge
    /// below preserves whatever it does not replace (#185).
    /// </summary>
    public IEnumerable<Guid>? Groups { get; init; }

    /// <summary>
    /// Property values to set, merged into the member's existing ones by alias + culture +
    /// segment. Null keeps them all.
    /// </summary>
    public IEnumerable<ContentValue>? Values { get; init; }

    /// <summary>
    /// A new password (#185). Set by an administrator, so no old password is required - the
    /// generated model has an <c>OldPassword</c> field for self-service changes, which this
    /// command does not cover.
    /// </summary>
    public string? NewPassword { get; init; }

    /// <summary>New locked-out state; false unlocks a member locked out by failed logins.</summary>
    public bool? IsLockedOut { get; init; }
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

/// <summary>Command-facing view of a dictionary tree item (issue #110): one row of the hierarchy.</summary>
public record DictionaryTreeItem
{
    /// <summary>The dictionary item id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The display name (the dictionary key).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Whether this item has child items.</summary>
    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; init; }

    /// <summary>The parent reference, or null at the dictionary root.</summary>
    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }
}

/// <summary>
/// A partial update to a dictionary item (#182). Translations are merged into the item's existing
/// ones by ISO code, so supplying one language leaves the others alone - the PUT itself is a full
/// replace, which is the shape that cost a test site its content in #179.
/// </summary>
public record UpdateDictionaryItemRequest
{
    /// <summary>New key/name, or null to keep the current one.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Translations to set; any language not named keeps its current value.</summary>
    [JsonPropertyName("translations")]
    public IEnumerable<DictionaryTranslation> Translations { get; init; } = [];
}

public record CreateDictionaryItemRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("translations")]
    public IEnumerable<DictionaryTranslation> Translations { get; init; } = [];

    /// <summary>Optional parent to create the item under; null creates it at the dictionary root (#110).</summary>
    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }
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
