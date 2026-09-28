using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// One document in a <see cref="ContentSnapshot"/>: the verbatim <c>GET /document/{id}</c> body
/// plus its tree placement. Unlike the schema entities, a document's raw GET body does not carry
/// its parent (placement lives in the tree, not the entity), so <see cref="Parent"/> is captured
/// separately during export and re-applied on create - it is the piece that lets a snapshot be
/// rebuilt in the right shape in another environment (issue #100 / ADR 0006).
/// </summary>
public sealed class ContentNode : ISnapshotTreeNode
{
    /// <summary>The document's GUID. Preserved across environments (apply recreates with this id).</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The parent document's id, or null when the document sits at the content root.</summary>
    [JsonPropertyName("parent")]
    public Guid? Parent { get; init; }

    /// <summary>The verbatim <c>GET /document/{id}</c> body (all variants, values, doc-type and template refs).</summary>
    [JsonPropertyName("body")]
    public JsonNode Body { get; init; } = new JsonObject();
}

/// <summary>
/// A portable, round-trippable dump of a content subtree - every document as its verbatim
/// Management-API JSON body plus its parent placement (issue #100 / ADR 0006). Produced by
/// <c>content export</c> and consumed by <c>content diff</c> / <c>content apply</c>.
///
/// Documents are stored in tree pre-order (a parent always precedes its children) so apply can
/// create them in an order that never references a not-yet-created parent. Because the entries are
/// the raw API bodies with the real document GUIDs, a snapshot exported from one instance can be
/// diffed/applied against another with stable cross-environment identity.
/// </summary>
public sealed class ContentSnapshot
{
    /// <summary>
    /// The snapshot layout version (currently <c>"1"</c>). Versions the file format so a future
    /// breaking change to the snapshot shape can be detected by <c>diff</c>/<c>apply</c>.
    /// </summary>
    [JsonPropertyName("contentVersion")]
    public string ContentVersion { get; init; } = CurrentVersion;

    /// <summary>The current snapshot layout version emitted by <c>content export</c>.</summary>
    public const string CurrentVersion = "1";

    /// <summary>
    /// The subtree root this snapshot was scoped to, or null for the whole content tree. Recorded
    /// so <c>diff</c>/<c>apply</c> compare against the <b>same</b> live scope: without it, a subtree
    /// snapshot applied with <c>--prune</c> would treat every document outside the subtree as
    /// "removed" and delete it. Diff/apply export the live tree with this same root.
    /// </summary>
    [JsonPropertyName("root")]
    public Guid? Root { get; init; }

    /// <summary>The documents in the subtree, in tree pre-order (parents before children).</summary>
    [JsonPropertyName("documents")]
    public List<ContentNode> Documents { get; init; } = [];

    /// <summary>
    /// The <see cref="JsonSerializerOptions"/> used to read and write snapshot files: indented for
    /// human-diffable output, camelCase to match the Management API, and null-tolerant. Shared so
    /// export, diff, and apply serialize identically.
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

    /// <summary>The JSON members that identify a document as a content snapshot.</summary>
    private static readonly string[] SnapshotMembers = ["contentVersion", "documents"];

    /// <summary>
    /// Parses a snapshot from JSON text. Tolerant of a CLI output <b>envelope</b>: if the text is a
    /// <c>{status,data,meta}</c> wrapper (e.g. piped straight from <c>content export</c> without
    /// <c>--out</c>), the inner <c>data</c> object is unwrapped so both the bare snapshot and the
    /// enveloped form load.
    ///
    /// This is the trust boundary for a destructive <c>apply --prune</c>: an arbitrary JSON object
    /// deserializes structurally into an all-empty snapshot, which a naive parse would then diff as
    /// "delete every document". So the text is <b>validated</b> to actually be a snapshot (it must
    /// carry a <c>contentVersion</c> or a <c>documents</c> array) and to be a supported version,
    /// throwing otherwise rather than returning a content-wiping empty snapshot.
    /// </summary>
    /// <param name="json">The snapshot JSON (bare or enveloped).</param>
    /// <returns>The parsed snapshot.</returns>
    /// <exception cref="JsonException">The text is not valid JSON, is not a snapshot, or is an unsupported version.</exception>
    public static ContentSnapshot FromJson(string json)
    {
        var root =
            JsonNode.Parse(json) ?? throw new JsonException("The snapshot is empty or null.");

        // Unwrap a CLI success envelope ({status, data:{...}, meta}) so a snapshot piped directly
        // from `content export` (which emits the envelope on stdout) still loads.
        if (
            root is JsonObject envelope
            && envelope["data"] is JsonObject data
            && SnapshotMembers.Any(data.ContainsKey)
        )
        {
            root = data;
        }

        // Reject anything that is not recognisably a snapshot BEFORE deserializing - otherwise an
        // arbitrary object (e.g. `{}`) becomes an all-empty snapshot that `apply --prune` reads as
        // "delete the entire live content tree".
        if (root is not JsonObject obj || !SnapshotMembers.Any(obj.ContainsKey))
            throw new JsonException(
                "Not a content snapshot: expected a 'contentVersion' and/or a 'documents' "
                    + "array (produced by 'content export')."
            );

        // Guard against a future snapshot format: refuse a version we do not understand rather than
        // mis-reading its shape.
        if (
            obj["contentVersion"] is JsonValue versionNode
            && versionNode.TryGetValue<string>(out var version)
            && version != CurrentVersion
        )
            throw new JsonException(
                $"Unsupported snapshot contentVersion '{version}' (this CLI writes and reads "
                    + $"'{CurrentVersion}'). Re-export with a matching CLI version."
            );

        return obj.Deserialize<ContentSnapshot>(SerializerOptions)
            ?? throw new JsonException("The snapshot could not be parsed.");
    }
}
