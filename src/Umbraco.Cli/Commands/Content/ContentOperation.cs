using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Content;

/// <summary>A content apply step (#223). Serialized in lower case, as the apply rows always were.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentOperation>))]
public enum ContentOperation
{
    /// <summary>Create a document with its snapshot id and parent.</summary>
    [JsonStringEnumMemberName("create")]
    Create,

    /// <summary>Replace a document's body.</summary>
    [JsonStringEnumMemberName("update")]
    Update,

    /// <summary>Unpublish cultures the snapshot does not have published.</summary>
    [JsonStringEnumMemberName("unpublish")]
    Unpublish,

    /// <summary>Publish cultures the snapshot has published.</summary>
    [JsonStringEnumMemberName("publish")]
    Publish,

    /// <summary>Delete a document the snapshot omits (prune).</summary>
    [JsonStringEnumMemberName("delete")]
    Delete,
}
