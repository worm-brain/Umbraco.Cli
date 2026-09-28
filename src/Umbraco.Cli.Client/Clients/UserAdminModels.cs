using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the user-administration nouns - user groups and user data
// (issue #109) - co-located with their client interfaces rather than in the shared Models.cs.
// Granular per-document permissions (#111) are surfaced as DocumentPermission; the other granular
// kinds (property-value, unknown) are not modelled, and the client carries them through updates.

/// <summary>
/// A granular permission on one document (#111): the verbs a user group has on that node, in place
/// of its fallback permissions.
/// </summary>
public record DocumentPermission
{
    /// <summary>The document id.</summary>
    [JsonPropertyName("document")]
    public Guid Document { get; init; }

    /// <summary>The permission verbs, e.g. <c>Umb.Document.Read</c>.</summary>
    [JsonPropertyName("verbs")]
    public IReadOnlyList<string> Verbs { get; init; } = [];
}

/// <summary>Command-facing view of a user group (issue #109).</summary>
public record UserGroupResponse
{
    /// <summary>The group's id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The unique alias used to reference the group in config and code.</summary>
    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The backoffice icon, e.g. <c>icon-users</c>.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>A free-text description of the group.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The backoffice section aliases the group can access.</summary>
    [JsonPropertyName("sections")]
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>The culture ISO codes the group can edit; empty when it has access to all.</summary>
    [JsonPropertyName("languages")]
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>The default permission verbs applied where no node-specific permission is set.</summary>
    [JsonPropertyName("fallbackPermissions")]
    public IReadOnlyList<string> FallbackPermissions { get; init; } = [];

    /// <summary>Whether the group can edit content in every language.</summary>
    [JsonPropertyName("hasAccessToAllLanguages")]
    public bool HasAccessToAllLanguages { get; init; }

    /// <summary>Whether the group's content start node is the content tree root.</summary>
    [JsonPropertyName("documentRootAccess")]
    public bool DocumentRootAccess { get; init; }

    /// <summary>Whether the group's media start node is the media tree root.</summary>
    [JsonPropertyName("mediaRootAccess")]
    public bool MediaRootAccess { get; init; }

    /// <summary>The content node the group's content tree starts at; null for none (#217).</summary>
    [JsonPropertyName("documentStartNode")]
    public Guid? DocumentStartNode { get; init; }

    /// <summary>The media node the group's media tree starts at; null for none (#217).</summary>
    [JsonPropertyName("mediaStartNode")]
    public Guid? MediaStartNode { get; init; }

    /// <summary>The group's granular per-document permissions (#111).</summary>
    [JsonPropertyName("documentPermissions")]
    public IReadOnlyList<DocumentPermission> DocumentPermissions { get; init; } = [];

    /// <summary>Whether the group may be deleted (built-in groups cannot).</summary>
    [JsonPropertyName("isDeletable")]
    public bool IsDeletable { get; init; }

    /// <summary>Whether the group's alias may be changed (built-in aliases are fixed).</summary>
    [JsonPropertyName("aliasCanBeChanged")]
    public bool AliasCanBeChanged { get; init; }
}

/// <summary>
/// Create payload for a user group (issue #109), with its granular per-document permissions
/// (#111).
/// </summary>
public record CreateUserGroupRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    /// <summary>The unique alias for the group.</summary>
    public required string Alias { get; init; }

    /// <summary>The group's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The backoffice icon; null lets Umbraco apply its default.</summary>
    public string? Icon { get; init; }

    /// <summary>A free-text description of the group.</summary>
    public string? Description { get; init; }

    /// <summary>The backoffice section aliases the group can access.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>The culture ISO codes the group can edit.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>The default permission verbs applied where no node-specific permission is set.</summary>
    public IReadOnlyList<string> FallbackPermissions { get; init; } = [];

    /// <summary>Whether the group can edit content in every language.</summary>
    public bool HasAccessToAllLanguages { get; init; }

    /// <summary>Whether the group's content start node is the content tree root.</summary>
    public bool DocumentRootAccess { get; init; }

    /// <summary>Whether the group's media start node is the media tree root.</summary>
    public bool MediaRootAccess { get; init; }

    /// <summary>The content node the group's content tree starts at; null for none (#217).</summary>
    [JsonPropertyName("documentStartNode")]
    public Guid? DocumentStartNode { get; init; }

    /// <summary>The media node the group's media tree starts at; null for none (#217).</summary>
    [JsonPropertyName("mediaStartNode")]
    public Guid? MediaStartNode { get; init; }

    /// <summary>The group's granular per-document permissions (#111); empty for none.</summary>
    public IReadOnlyList<DocumentPermission> DocumentPermissions { get; init; } = [];
}

/// <summary>
/// Update payload for a user group (issue #109). Mirrors <see cref="CreateUserGroupRequest"/>
/// without the id (the id is a route parameter).
/// </summary>
public record UpdateUserGroupRequest
{
    /// <summary>The unique alias for the group.</summary>
    public required string Alias { get; init; }

    /// <summary>The group's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The backoffice icon; null lets Umbraco apply its default.</summary>
    public string? Icon { get; init; }

    /// <summary>A free-text description of the group.</summary>
    public string? Description { get; init; }

    /// <summary>The backoffice section aliases the group can access.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>The culture ISO codes the group can edit.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>The default permission verbs applied where no node-specific permission is set.</summary>
    public IReadOnlyList<string> FallbackPermissions { get; init; } = [];

    /// <summary>Whether the group can edit content in every language.</summary>
    public bool HasAccessToAllLanguages { get; init; }

    /// <summary>Whether the group's content start node is the content tree root.</summary>
    public bool DocumentRootAccess { get; init; }

    /// <summary>Whether the group's media start node is the media tree root.</summary>
    public bool MediaRootAccess { get; init; }

    /// <summary>The content node the group's content tree starts at; null for none (#217).</summary>
    [JsonPropertyName("documentStartNode")]
    public Guid? DocumentStartNode { get; init; }

    /// <summary>The media node the group's media tree starts at; null for none (#217).</summary>
    [JsonPropertyName("mediaStartNode")]
    public Guid? MediaStartNode { get; init; }

    /// <summary>
    /// The group's complete set of granular per-document permissions (#111), or null to keep the
    /// ones it has. Null is the default so a caller that does not deal in permissions (the schema
    /// applier, say) can never wipe them; the other granular kinds are always kept.
    /// </summary>
    public IReadOnlyList<DocumentPermission>? DocumentPermissions { get; init; }
}

/// <summary>
/// Command-facing view of a user-data entry (issue #109): a key/value record scoped to the
/// authenticated user, addressed by <see cref="Key"/> and grouped under <see cref="Group"/>.
/// </summary>
public record UserDataResponse
{
    /// <summary>The entry's key (its unique id).</summary>
    [JsonPropertyName("key")]
    public Guid Key { get; init; }

    /// <summary>The group the entry belongs to (a namespace for identifiers).</summary>
    [JsonPropertyName("group")]
    public string Group { get; init; } = "";

    /// <summary>The identifier of the entry within its group.</summary>
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = "";

    /// <summary>The stored value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

/// <summary>Create payload for a user-data entry (issue #109).</summary>
public record CreateUserDataRequest
{
    /// <summary>Caller-supplied key for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Key { get; init; }

    /// <summary>The group the entry belongs to.</summary>
    public required string Group { get; init; }

    /// <summary>The identifier of the entry within its group.</summary>
    public required string Identifier { get; init; }

    /// <summary>The value to store.</summary>
    public required string Value { get; init; }
}

/// <summary>
/// Update payload for a user-data entry (issue #109). The Management API updates user data with a
/// collection-level PUT that carries the target <see cref="Key"/> in the body rather than the URL.
/// </summary>
public record UpdateUserDataRequest
{
    /// <summary>The key of the entry to update.</summary>
    public required Guid Key { get; init; }

    /// <summary>The group the entry belongs to.</summary>
    public required string Group { get; init; }

    /// <summary>The identifier of the entry within its group.</summary>
    public required string Identifier { get; init; }

    /// <summary>The new value to store.</summary>
    public required string Value { get; init; }
}
