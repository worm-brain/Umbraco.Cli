using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the redirect and relation nouns (issue #118). relation-type and
// relation are read-only in the generated client; redirect adds delete + a tracking toggle.

/// <summary>A tracked URL redirect (issue #118).</summary>
public record RedirectResponse
{
    /// <summary>The redirect's id (what <c>redirect delete</c> targets).</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The original (old) URL that now redirects.</summary>
    [JsonPropertyName("originalUrl")]
    public string OriginalUrl { get; init; } = "";

    /// <summary>The destination (new) URL redirected to.</summary>
    [JsonPropertyName("destinationUrl")]
    public string DestinationUrl { get; init; } = "";

    /// <summary>The culture the redirect applies to, if any.</summary>
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    /// <summary>The id of the destination document (content key), if any.</summary>
    [JsonPropertyName("contentKey")]
    public Guid? ContentKey { get; init; }

    /// <summary>When the redirect was created.</summary>
    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; init; }
}

/// <summary>The URL-tracking status for redirects (issue #118).</summary>
public record RedirectStatusResponse
{
    /// <summary>Whether automatic redirect tracking is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>Whether the current user is an admin (required to toggle tracking).</summary>
    [JsonPropertyName("userIsAdmin")]
    public bool UserIsAdmin { get; init; }
}

/// <summary>Command-facing view of a relation type (issue #118).</summary>
public record RelationTypeResponse
{
    /// <summary>The relation type's id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The unique alias.</summary>
    [JsonPropertyName("alias")]
    public string Alias { get; init; } = "";

    /// <summary>The display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Whether the relation is bidirectional (parent&lt;-&gt;child both ways).</summary>
    [JsonPropertyName("isBidirectional")]
    public bool IsBidirectional { get; init; }

    /// <summary>Whether the relation is a dependency (deleting the parent affects the child).</summary>
    [JsonPropertyName("isDependency")]
    public bool IsDependency { get; init; }

    /// <summary>The name of the parent object type (e.g. Document, Media).</summary>
    [JsonPropertyName("parentObjectType")]
    public string? ParentObjectType { get; init; }

    /// <summary>The name of the child object type.</summary>
    [JsonPropertyName("childObjectType")]
    public string? ChildObjectType { get; init; }
}

/// <summary>A single relation between two entities (issue #118).</summary>
public record RelationResponse
{
    /// <summary>The relation's id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The parent entity id.</summary>
    [JsonPropertyName("parentId")]
    public Guid ParentId { get; init; }

    /// <summary>The parent entity name.</summary>
    [JsonPropertyName("parentName")]
    public string ParentName { get; init; } = "";

    /// <summary>The child entity id.</summary>
    [JsonPropertyName("childId")]
    public Guid ChildId { get; init; }

    /// <summary>The child entity name.</summary>
    [JsonPropertyName("childName")]
    public string ChildName { get; init; } = "";

    /// <summary>The id of the relation type this relation belongs to.</summary>
    [JsonPropertyName("relationTypeId")]
    public Guid RelationTypeId { get; init; }

    /// <summary>A free-text comment on the relation, if any.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>When the relation was created.</summary>
    [JsonPropertyName("createDate")]
    public DateTimeOffset CreateDate { get; init; }
}
