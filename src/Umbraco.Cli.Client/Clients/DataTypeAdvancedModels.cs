using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the data-type advanced verbs and folders (issue #121). The
// referenced-by result is a polymorphic union in the generated client, so it is surfaced as raw
// JSON (see IDataTypeClient.GetDataTypeReferencedByRawAsync) rather than a typed record here.

/// <summary>A data-type folder (issue #121).</summary>
public record DataTypeFolderResponse
{
    /// <summary>The folder id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The folder name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Create payload for a data-type folder (issue #121).</summary>
public record CreateDataTypeFolderRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    /// <summary>The folder name.</summary>
    public required string Name { get; init; }

    /// <summary>The parent folder id, or null for the data-type tree root.</summary>
    public Guid? ParentId { get; init; }
}
