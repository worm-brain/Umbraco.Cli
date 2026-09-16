using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for Examine (indexer/searcher) and imaging (issue #121).

/// <summary>An Examine index (issue #121).</summary>
public record IndexResponse
{
    /// <summary>The index name (its key).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The index health status (e.g. Healthy, Unhealthy, Rebuilding).</summary>
    [JsonPropertyName("healthStatus")]
    public string? HealthStatus { get; init; }

    /// <summary>The number of documents in the index.</summary>
    [JsonPropertyName("documentCount")]
    public long DocumentCount { get; init; }

    /// <summary>The number of fields in the index.</summary>
    [JsonPropertyName("fieldCount")]
    public int FieldCount { get; init; }

    /// <summary>Whether the index can be rebuilt.</summary>
    [JsonPropertyName("canRebuild")]
    public bool CanRebuild { get; init; }

    /// <summary>The name of the searcher that reads this index.</summary>
    [JsonPropertyName("searcherName")]
    public string? SearcherName { get; init; }
}

/// <summary>An Examine searcher (issue #121).</summary>
public record SearcherResponse
{
    /// <summary>The searcher name (its key).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>A single search result from a searcher query (issue #121).</summary>
public record SearchResultResponse
{
    /// <summary>The result document id (Examine ids are strings).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>The relevance score.</summary>
    [JsonPropertyName("score")]
    public float Score { get; init; }

    /// <summary>The indexed fields returned with the result.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<SearchResultField> Fields { get; init; } = [];
}

/// <summary>One field of a search result (issue #121).</summary>
public record SearchResultField
{
    /// <summary>The field name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The field's values.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>The resized URLs for one media item (issue #121).</summary>
public record MediaResizeUrlResponse
{
    /// <summary>The media item id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The resized URLs, one per culture.</summary>
    [JsonPropertyName("urls")]
    public IReadOnlyList<MediaResizeUrl> Urls { get; init; } = [];
}

/// <summary>A single resized media URL for a culture (issue #121).</summary>
public record MediaResizeUrl
{
    /// <summary>The culture the URL applies to, if any.</summary>
    [JsonPropertyName("culture")]
    public string? Culture { get; init; }

    /// <summary>The resized image URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>The crop mode for image resizing (issue #121).</summary>
public enum ImageResizeMode
{
    /// <summary>Crop to fill the target dimensions.</summary>
    Crop,

    /// <summary>Resize so the image fits within the target dimensions.</summary>
    Max,

    /// <summary>Stretch to the target dimensions (ignores aspect ratio).</summary>
    Stretch,

    /// <summary>Pad to the target dimensions.</summary>
    Pad,

    /// <summary>Box-pad to the target dimensions.</summary>
    BoxPad,

    /// <summary>Resize so the image covers at least the target dimensions.</summary>
    Min,
}
