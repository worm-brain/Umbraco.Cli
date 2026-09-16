using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the small coverage resources - member groups, tags, cultures
// (issue #107) - co-located with their client interfaces rather than in the shared Models.cs.

/// <summary>Command-facing view of a member group (issue #107): a named group of members.</summary>
public record MemberGroupResponse
{
    /// <summary>The group's id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The group name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Create payload for a member group (issue #107).</summary>
public record CreateMemberGroupRequest
{
    /// <summary>Caller-supplied id for an idempotent create (#86); a GUID is generated if null.</summary>
    public Guid? Id { get; init; }

    /// <summary>The group name.</summary>
    public required string Name { get; init; }
}

/// <summary>Update payload for a member group (issue #107). Only the name is mutable.</summary>
public record UpdateMemberGroupRequest
{
    /// <summary>The new group name.</summary>
    public required string Name { get; init; }
}

/// <summary>Command-facing view of a tag (issue #107).</summary>
public record TagResponse
{
    /// <summary>The tag's id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The tag text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>The tag group the tag belongs to.</summary>
    [JsonPropertyName("group")]
    public string Group { get; init; } = "";

    /// <summary>How many nodes are tagged with this tag.</summary>
    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; init; }
}

/// <summary>Command-facing view of an available culture (issue #107).</summary>
public record CultureResponse
{
    /// <summary>The culture's ISO code (e.g. <c>en-US</c>).</summary>
    [JsonPropertyName("isoCode")]
    public string IsoCode { get; init; } = "";

    /// <summary>The culture's English name.</summary>
    [JsonPropertyName("englishName")]
    public string EnglishName { get; init; } = "";
}
