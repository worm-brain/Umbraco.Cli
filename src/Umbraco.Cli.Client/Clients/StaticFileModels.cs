using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the static-file resources (scripts, stylesheets, partial views),
// co-located with IStaticFileClient (issue #105) rather than in the shared Models.cs grab-bag.

/// <summary>
/// Command-facing view of a static file - script, stylesheet, or partial view (issue #105).
/// Populated from a single-item GET, which carries the full <see cref="Content"/>; the list view
/// (backed by the file-system tree) carries only path/name.
/// </summary>
public record StaticFileResponse
{
    /// <summary>The file's path, which is its identity (there is no GUID).</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>The file name (last path segment).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The parent folder path, or null at the tree root.</summary>
    [JsonPropertyName("parentPath")]
    public string? ParentPath { get; init; }

    /// <summary>The file's full text content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>A static-file tree node (file or folder) from a list, identified by path (issue #105).</summary>
public record StaticFileTreeItem
{
    /// <summary>The node's path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>The node's name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>True when the node is a folder rather than a file.</summary>
    [JsonPropertyName("isFolder")]
    public bool IsFolder { get; init; }

    /// <summary>True when the node has children (folders only).</summary>
    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; init; }
}

/// <summary>A static-file folder (#238), as <c>GET /{kind}/folder/{path}</c> returns it.</summary>
public record StaticFileFolderResponse
{
    /// <summary>The folder's path, in Umbraco's <c>/a/b</c> form; its identity.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>The folder name (last path segment).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The parent folder path, or null at the tree root.</summary>
    [JsonPropertyName("parentPath")]
    public string? ParentPath { get; init; }
}

/// <summary>Create payload for a static file (issue #105).</summary>
public record CreateStaticFileRequest
{
    /// <summary>The file name, e.g. <c>site.js</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The parent folder path, or null to create at the tree root.</summary>
    public string? ParentPath { get; init; }

    /// <summary>The file's text content (empty by default).</summary>
    public string Content { get; init; } = "";
}

/// <summary>Update payload for a static file (issue #105). Only the content is updatable; a rename uses a separate endpoint.</summary>
public record UpdateStaticFileRequest
{
    /// <summary>The new file content.</summary>
    public required string Content { get; init; }
}
