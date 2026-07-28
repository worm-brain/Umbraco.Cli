using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// A portable, round-trippable dump of an Umbraco instance's schema — every document type,
/// data type, and template — as the **verbatim** Management-API JSON body of each entity
/// (issue #68 / ADR 0004 §1, "raw-JSON passthrough"). Produced by <c>schema export</c> and
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
    /// The snapshot **layout** version (currently <c>"1"</c>). Independent of the CLI output
    /// envelope's <c>meta.schemaVersion</c> — this versions the file format so a future
    /// breaking change to the snapshot shape can be detected by <c>diff</c>/<c>apply</c>.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = CurrentVersion;

    /// <summary>The current snapshot layout version emitted by <c>schema export</c>.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Verbatim <c>GET /document-type/{id}</c> bodies, one per document type.</summary>
    [JsonPropertyName("documentTypes")]
    public List<JsonNode> DocumentTypes { get; init; } = [];

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

    /// <summary>
    /// Parses a snapshot from JSON text. Tolerant of a CLI output **envelope**: if the text is
    /// a <c>{status,data,meta}</c> wrapper (e.g. piped straight from <c>schema export</c>
    /// without <c>--out</c>), the inner <c>data</c> object is unwrapped so both the bare
    /// snapshot and the enveloped form load. Throws <see cref="JsonException"/> on malformed
    /// input or when no schema arrays are present.
    /// </summary>
    /// <param name="json">The snapshot JSON (bare or enveloped).</param>
    /// <returns>The parsed snapshot.</returns>
    /// <exception cref="JsonException">The text is not valid JSON or is not a snapshot.</exception>
    public static SchemaSnapshot FromJson(string json)
    {
        var root =
            JsonNode.Parse(json) ?? throw new JsonException("The snapshot is empty or null.");

        // Unwrap a CLI success envelope ({status, data:{...}, meta}) so a snapshot piped
        // directly from `schema export` (which emits the envelope on stdout) still loads.
        if (
            root is JsonObject obj
            && obj.ContainsKey("data")
            && obj["data"] is JsonObject data
            && (
                data.ContainsKey("documentTypes")
                || data.ContainsKey("dataTypes")
                || data.ContainsKey("templates")
            )
        )
        {
            root = data;
        }

        return root.Deserialize<SchemaSnapshot>(SerializerOptions)
            ?? throw new JsonException("The snapshot could not be parsed.");
    }
}
