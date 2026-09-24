using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// A portable, round-trippable dump of an Umbraco instance's schema — every document type,
/// media type, member type, data type, and template — as the **verbatim** Management-API JSON
/// body of each entity
/// (issue #68 / ADR 0005 §1, "raw-JSON passthrough"). Produced by <c>schema export</c> and
/// consumed by <c>schema diff</c> / <c>schema apply</c>.
///
/// The bodies are stored as <see cref="JsonNode"/> rather than typed records precisely so
/// nothing is dropped: the CLI's own response records omit doc-type properties/compositions,
/// data-type config values, and template Razor. Because the entries are the raw API bodies,
/// a snapshot exported from one instance can be diffed/applied against another.
/// </summary>
public sealed class SchemaSnapshot
{
    /// <summary>
    /// The snapshot **layout** version (currently <c>"2"</c>). Independent of the CLI output
    /// envelope's <c>meta.schemaVersion</c> — this versions the file format so a future
    /// breaking change to the snapshot shape can be detected by <c>diff</c>/<c>apply</c>.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = CurrentVersion;

    /// <summary>The current snapshot layout version emitted by <c>schema export</c>.</summary>
    /// <remarks>
    /// Bumped to <c>"2"</c> when media types and member types joined the snapshot (#186). A
    /// version-1 file is <b>refused</b> rather than read as an empty-for-those-kinds snapshot:
    /// <c>apply --prune</c> would diff the missing arrays as "delete every media type and member
    /// type on the instance". Re-export instead.
    /// </remarks>
    public const string CurrentVersion = "2";

    /// <summary>Verbatim <c>GET /document-type/{id}</c> bodies, one per document type.</summary>
    [JsonPropertyName("documentTypes")]
    public List<JsonNode> DocumentTypes { get; init; } = [];

    /// <summary>Verbatim <c>GET /media-type/{id}</c> bodies, one per media type (#186).</summary>
    [JsonPropertyName("mediaTypes")]
    public List<JsonNode> MediaTypes { get; init; } = [];

    /// <summary>Verbatim <c>GET /member-type/{id}</c> bodies, one per member type (#186).</summary>
    [JsonPropertyName("memberTypes")]
    public List<JsonNode> MemberTypes { get; init; } = [];

    /// <summary>Verbatim <c>GET /data-type/{id}</c> bodies, one per data type.</summary>
    [JsonPropertyName("dataTypes")]
    public List<JsonNode> DataTypes { get; init; } = [];

    /// <summary>Verbatim <c>GET /template/{id}</c> bodies, one per template.</summary>
    [JsonPropertyName("templates")]
    public List<JsonNode> Templates { get; init; } = [];

    /// <summary>
    /// The <see cref="JsonSerializerOptions"/> used to read and write snapshot files: indented
    /// for human-diffable output, camelCase to match the Management API, and null-tolerant.
    /// Shared so export, diff, and apply serialize identically.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes this snapshot to indented JSON (the on-disk / stdout snapshot form).</summary>
    /// <returns>The snapshot as an indented JSON string.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>The JSON members that identify a document as a snapshot.</summary>
    private static readonly string[] SnapshotMembers =
    [
        "schemaVersion",
        "documentTypes",
        "mediaTypes",
        "memberTypes",
        "dataTypes",
        "templates",
    ];

    /// <summary>
    /// Parses a snapshot from JSON text. Tolerant of a CLI output **envelope**: if the text is
    /// a <c>{status,data,meta}</c> wrapper (e.g. piped straight from <c>schema export</c>
    /// without <c>--out</c>), the inner <c>data</c> object is unwrapped so both the bare
    /// snapshot and the enveloped form load.
    ///
    /// This is the trust boundary for a destructive <c>apply --prune</c>: any random JSON object
    /// deserializes structurally into an all-empty snapshot, which a naive parse would then diff
    /// as "delete everything". So the text is <b>validated</b> to actually be a snapshot (it must
    /// carry a <c>schemaVersion</c> or at least one of the schema arrays) and to be a supported
    /// version, throwing otherwise rather than returning a schema-wiping empty snapshot.
    /// </summary>
    /// <param name="json">The snapshot JSON (bare or enveloped).</param>
    /// <returns>The parsed snapshot.</returns>
    /// <exception cref="JsonException">The text is not valid JSON, is not a snapshot, or is an unsupported version.</exception>
    public static SchemaSnapshot FromJson(string json)
    {
        var root =
            JsonNode.Parse(json) ?? throw new JsonException("The snapshot is empty or null.");

        // Unwrap a CLI success envelope ({status, data:{...}, meta}) so a snapshot piped
        // directly from `schema export` (which emits the envelope on stdout) still loads.
        if (
            root is JsonObject envelope
            && envelope["data"] is JsonObject data
            && SnapshotMembers.Any(data.ContainsKey)
        )
        {
            root = data;
        }

        // Reject anything that is not recognisably a snapshot BEFORE deserializing — otherwise an
        // arbitrary object (e.g. `{}`) becomes an all-empty snapshot that `apply --prune` reads as
        // "delete the entire live schema".
        if (root is not JsonObject obj || !SnapshotMembers.Any(obj.ContainsKey))
            throw new JsonException(
                "Not a schema snapshot: expected a 'schemaVersion' and/or "
                    + "documentTypes/mediaTypes/memberTypes/dataTypes/templates arrays "
                    + "(produced by 'schema export')."
            );

        // Guard against a future snapshot format: refuse a version we do not understand rather
        // than mis-reading its shape.
        if (
            obj["schemaVersion"] is JsonValue versionNode
            && versionNode.TryGetValue<string>(out var version)
            && version != CurrentVersion
        )
            throw new JsonException(
                $"Unsupported snapshot schemaVersion '{version}' (this CLI writes and reads "
                    + $"'{CurrentVersion}'). Re-export with a matching CLI version."
            );

        return obj.Deserialize<SchemaSnapshot>(SerializerOptions)
            ?? throw new JsonException("The snapshot could not be parsed.");
    }
}
