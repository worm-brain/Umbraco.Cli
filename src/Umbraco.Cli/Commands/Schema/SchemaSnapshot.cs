using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// A portable, round-trippable dump of an Umbraco instance's schema — every document type,
/// media type, member type, data type, template, language, dictionary item, member group and
/// user group — as the **verbatim** Management-API JSON body of each entity
/// (issue #68 / ADR 0005 §1, "raw-JSON passthrough"), plus the partial views, stylesheets and
/// scripts the templates need (#292). Produced by <c>schema export</c> and consumed by
/// <c>schema diff</c> / <c>schema apply</c>.
///
/// The bodies are stored as <see cref="JsonNode"/> rather than typed records precisely so
/// nothing is dropped: the CLI's own response records omit doc-type properties/compositions,
/// data-type config values, and template Razor. Because the entries are the raw API bodies,
/// a snapshot exported from one instance can be diffed/applied against another.
/// </summary>
public sealed class SchemaSnapshot
{
    /// <summary>
    /// The snapshot **layout** version (currently <c>"4"</c>). Independent of the CLI output
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
    /// <para>
    /// Bumped to <c>"3"</c> when languages, the dictionary and member and user groups joined it
    /// (#227). A version-2 file is refused for the same reason.
    /// </para>
    /// <para>
    /// Bumped to <c>"4"</c> when partial views, stylesheets and scripts joined it (#292). A
    /// version-3 file is still read: it has no file sections, and an absent section means "files
    /// not managed", so diff and apply leave the instance's files alone rather than pruning them.
    /// </para>
    /// </remarks>
    public const string CurrentVersion = "4";

    /// <summary>The layout versions this CLI reads: the current one, and "3", which predates the file sections.</summary>
    public static readonly IReadOnlyList<string> ReadableVersions = ["3", CurrentVersion];

    // Every section below is nullable, and null means ABSENT: the snapshot does not manage that
    // kind, so diff and apply skip it and --prune deletes none of it (#292 for the files, #198 for
    // the rest). A snapshot built in code starts with every type section present and empty, as an
    // export would make it; FromJson makes a section the file leaves out absent, which is what
    // lets a hand-written snapshot carry only the kinds it means to change.

    /// <summary>Verbatim <c>GET /document-type/{id}</c> bodies, one per document type.</summary>
    [JsonPropertyName("documentTypes")]
    public List<JsonNode>? DocumentTypes { get; set; } = [];

    /// <summary>Verbatim <c>GET /media-type/{id}</c> bodies, one per media type (#186).</summary>
    [JsonPropertyName("mediaTypes")]
    public List<JsonNode>? MediaTypes { get; set; } = [];

    /// <summary>Verbatim <c>GET /member-type/{id}</c> bodies, one per member type (#186).</summary>
    [JsonPropertyName("memberTypes")]
    public List<JsonNode>? MemberTypes { get; set; } = [];

    /// <summary>Verbatim <c>GET /data-type/{id}</c> bodies, one per data type.</summary>
    [JsonPropertyName("dataTypes")]
    public List<JsonNode>? DataTypes { get; set; } = [];

    /// <summary>Verbatim <c>GET /template/{id}</c> bodies, one per template.</summary>
    [JsonPropertyName("templates")]
    public List<JsonNode>? Templates { get; set; } = [];

    /// <summary>Verbatim <c>GET /language</c> items, one per language (#227). Keyed by <c>isoCode</c>.</summary>
    [JsonPropertyName("languages")]
    public List<JsonNode>? Languages { get; set; } = [];

    /// <summary>
    /// <c>GET /dictionary/{id}</c> bodies, one per dictionary item (#227), with the item's
    /// <c>parent</c> added (the item read has none) and its translations sorted by ISO code.
    /// </summary>
    [JsonPropertyName("dictionaryItems")]
    public List<JsonNode>? DictionaryItems { get; set; } = [];

    /// <summary>Verbatim <c>GET /member-group/{id}</c> bodies, one per member group (#227).</summary>
    [JsonPropertyName("memberGroups")]
    public List<JsonNode>? MemberGroups { get; set; } = [];

    /// <summary>
    /// <c>GET /user-group/{id}</c> bodies, one per user group (#227), without the parts that name
    /// content on one instance: the document and media start nodes and the per-document
    /// permissions. See <see cref="SchemaBodies.PortableUserGroup"/>.
    /// </summary>
    [JsonPropertyName("userGroups")]
    public List<JsonNode>? UserGroups { get; set; } = [];

    /// <summary>
    /// Partial views and their folders (#292): <c>{path, content}</c> per file and
    /// <c>{path, isFolder: true}</c> per folder (see <see cref="SchemaStaticFiles"/>).
    /// <para>
    /// <b>Null means the section is absent</b>, and an absent section does not manage partial
    /// views: diff and apply skip them and <c>--prune</c> deletes none. That is every format "3"
    /// file and every <c>schema export --no-files</c>. An empty array is different: it manages
    /// them, and says there are none, so a prune deletes the live ones.
    /// </para>
    /// </summary>
    [JsonPropertyName("partialViews")]
    public List<JsonNode>? PartialViews { get; set; }

    /// <summary>Stylesheets and their folders (#292); null when the section is absent (see <see cref="PartialViews"/>).</summary>
    [JsonPropertyName("stylesheets")]
    public List<JsonNode>? Stylesheets { get; set; }

    /// <summary>Scripts and their folders (#292); null when the section is absent (see <see cref="PartialViews"/>).</summary>
    [JsonPropertyName("scripts")]
    public List<JsonNode>? Scripts { get; set; }

    /// <summary>Whether this snapshot manages any static-file kind (has at least one file section).</summary>
    [JsonIgnore]
    public bool ManagesFiles =>
        SchemaKinds.All.Any(k => k.File is not null && k.Section(this) is not null);

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
    /// The JSON members that identify a document as a snapshot: the version and every kind's
    /// section, from the kind table (#273).
    /// </summary>
    private static readonly string[] SnapshotMembers =
    [
        "schemaVersion",
        .. SchemaKinds.All.Select(k => k.Member),
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
                    + "documentTypes/mediaTypes/memberTypes/dataTypes/templates/languages/"
                    + "dictionaryItems/memberGroups/userGroups/partialViews/stylesheets/scripts "
                    + "arrays (produced by 'schema export')."
            );

        // Guard against another snapshot format: refuse a version we do not understand rather
        // than mis-reading its shape. "3" is still read: it only lacks the file sections, which
        // then count as not managed (#292).
        if (
            obj["schemaVersion"] is JsonValue versionNode
            && versionNode.TryGetValue<string>(out var version)
            && !ReadableVersions.Contains(version)
        )
            throw new JsonException(
                $"Unsupported snapshot schemaVersion '{version}' (this CLI writes "
                    + $"'{CurrentVersion}' and reads {string.Join(" and ", ReadableVersions.Select(v => $"'{v}'"))}). "
                    + "Re-export with a matching CLI version."
            );

        var snapshot =
            obj.Deserialize<SchemaSnapshot>(SerializerOptions)
            ?? throw new JsonException("The snapshot could not be parsed.");

        // #198: a section the file leaves out (or sets to null) is absent, not empty, so a
        // hand-written snapshot with only the kinds it changes never prunes the rest. Every
        // exported file has every type section, so this changes nothing for them.
        foreach (var kind in SchemaKinds.All)
            if (!HasSection(obj, kind.Member))
                kind.SetSection(snapshot, null);
        return snapshot;
    }

    /// <summary>Whether a snapshot object carries a non-null member, matched ignoring case as the deserializer does.</summary>
    /// <param name="obj">The snapshot object.</param>
    /// <param name="member">The section member, e.g. <c>documentTypes</c>.</param>
    /// <returns>True when the section is present.</returns>
    private static bool HasSection(JsonObject obj, string member) =>
        obj.Any(p =>
            string.Equals(p.Key, member, StringComparison.OrdinalIgnoreCase) && p.Value is not null
        );
}
