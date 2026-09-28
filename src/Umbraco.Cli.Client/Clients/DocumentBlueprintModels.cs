using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the document-blueprint noun (issue #113). A document blueprint (aka
// content template) is a pre-filled document used as a starting point when authors create content.
// Its create/update body is structurally identical to a document's, so the shared ContentValue /
// ContentVariant / ContentTypeReference / ContentParentReference records (Models.cs) are reused
// here rather than duplicated. get/scaffold/create return the raw JSON (JsonNode) for full fidelity;
// only the tree-listing and folder shapes need typed projections.

/// <summary>
/// Create payload for a document blueprint (issue #113). Deserialised from <c>--json-body</c> as
/// well as built from flags, so the JSON property names match the Management API body shape.
/// </summary>
public record CreateDocumentBlueprintRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>The document type the blueprint is based on (by alias or id).</summary>
    [JsonPropertyName("documentType")]
    public required ContentTypeReference DocumentType { get; init; }

    /// <summary>The parent folder, or null to create at the blueprint root.</summary>
    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }

    /// <summary>The property values pre-filled by the blueprint.</summary>
    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];

    /// <summary>The variants (name/culture/segment) of the blueprint.</summary>
    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariant> Variants { get; init; } = [];
}

/// <summary>
/// Update payload for a document blueprint (issue #113). The Management API's update contract is
/// only values + variants (the document type and placement are immutable), so - unlike member-type -
/// a typed PUT is not lossy and no read-merge is needed.
/// </summary>
public record UpdateDocumentBlueprintRequest
{
    /// <summary>The property values to write.</summary>
    [JsonPropertyName("values")]
    public IEnumerable<ContentValue> Values { get; init; } = [];

    /// <summary>The variants to write.</summary>
    [JsonPropertyName("variants")]
    public IEnumerable<ContentVariant> Variants { get; init; } = [];
}

/// <summary>
/// Payload to scaffold a blueprint from an existing document (issue #113): <c>from-document</c>.
/// </summary>
public record CreateBlueprintFromDocumentRequest
{
    /// <summary>Caller-supplied id for the new blueprint (#86); a GUID is generated if null.</summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>The id of the source document to copy into a blueprint.</summary>
    [JsonPropertyName("document")]
    public required Guid Document { get; init; }

    /// <summary>The name for the new blueprint.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The parent folder, or null for the blueprint root.</summary>
    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }
}

/// <summary>Command-facing view of a blueprint tree item (issue #113): one row of the listing.</summary>
public record DocumentBlueprintTreeItem
{
    /// <summary>The blueprint (or folder) id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// The document type the blueprint is based on; null for folders. Its alias is looked up
    /// (#361) and left out when that read fails, rather than shown as <c>""</c>.
    /// </summary>
    [JsonPropertyName("documentType")]
    public ContentTypeRef? DocumentType { get; init; }

    /// <summary>Whether this item is a folder rather than a blueprint.</summary>
    [JsonPropertyName("isFolder")]
    public bool IsFolder { get; init; }

    /// <summary>Whether this item has children.</summary>
    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; init; }

    /// <summary>The parent folder reference, or null at the root.</summary>
    [JsonPropertyName("parent")]
    public ContentParentReference? Parent { get; init; }
}

/// <summary>Command-facing view of a blueprint folder (issue #113).</summary>
public record BlueprintFolderResponse
{
    /// <summary>The folder id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The folder name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Create payload for a blueprint folder (issue #113).</summary>
public record CreateBlueprintFolderRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    /// <summary>The folder name.</summary>
    public required string Name { get; init; }

    /// <summary>The parent folder, or null for the blueprint root.</summary>
    public ContentParentReference? Parent { get; init; }
}
