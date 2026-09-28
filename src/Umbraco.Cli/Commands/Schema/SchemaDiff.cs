using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The schema entity-kind tags (issue #68, extended by #186 and #227), shared by the diff engine,
/// applier, and command output so the strings are defined once rather than re-declared per file.
/// </summary>
public static class SchemaKinds
{
    /// <summary>Document type kind tag.</summary>
    public const string DocumentType = "documentType";

    /// <summary>Media type kind tag (#186).</summary>
    public const string MediaType = "mediaType";

    /// <summary>Member type kind tag (#186).</summary>
    public const string MemberType = "memberType";

    /// <summary>Data type kind tag.</summary>
    public const string DataType = "dataType";

    /// <summary>Template kind tag.</summary>
    public const string Template = "template";

    /// <summary>Language kind tag (#227). Languages are keyed by ISO code and have no id.</summary>
    public const string Language = "language";

    /// <summary>Dictionary item kind tag (#227), keyed by the item's key (its name).</summary>
    public const string DictionaryItem = "dictionaryItem";

    /// <summary>Member group kind tag (#227), keyed by name.</summary>
    public const string MemberGroup = "memberGroup";

    /// <summary>User group kind tag (#227), keyed by alias.</summary>
    public const string UserGroup = "userGroup";

    /// <summary>Partial view kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string PartialView = "partialView";

    /// <summary>Stylesheet kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string Stylesheet = "stylesheet";

    /// <summary>Script kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string Script = "script";

    /// <summary>The client's <see cref="Umbraco.Cli.Client.StaticFileKind"/> for a static-file kind tag.</summary>
    /// <param name="kind">The kind tag.</param>
    /// <returns>The static-file kind, or null when the tag is not a static-file kind.</returns>
    public static Umbraco.Cli.Client.StaticFileKind? StaticFileKindOf(string kind) =>
        kind switch
        {
            PartialView => Umbraco.Cli.Client.StaticFileKind.PartialView,
            Stylesheet => Umbraco.Cli.Client.StaticFileKind.Stylesheet,
            Script => Umbraco.Cli.Client.StaticFileKind.Script,
            _ => null,
        };

    /// <summary>The client's <see cref="Umbraco.Cli.Client.EntityKind"/> for a snapshot kind tag.</summary>
    /// <param name="kind">The kind tag, e.g. <see cref="DocumentType"/>.</param>
    /// <returns>The entity kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The tag is not a schema kind, or is <see cref="Language"/>, which has no id and so no entity kind.
    /// </exception>
    public static Umbraco.Cli.Client.EntityKind EntityOf(string kind) =>
        kind switch
        {
            DocumentType => Umbraco.Cli.Client.EntityKind.DocumentType,
            MediaType => Umbraco.Cli.Client.EntityKind.MediaType,
            MemberType => Umbraco.Cli.Client.EntityKind.MemberType,
            DataType => Umbraco.Cli.Client.EntityKind.DataType,
            Template => Umbraco.Cli.Client.EntityKind.Template,
            DictionaryItem => Umbraco.Cli.Client.EntityKind.DictionaryItem,
            MemberGroup => Umbraco.Cli.Client.EntityKind.MemberGroup,
            UserGroup => Umbraco.Cli.Client.EntityKind.UserGroup,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a schema kind."),
        };
}

/// <summary>
/// How a single schema entity differs between the desired snapshot and the live instance
/// (issue #68 / ADR 0005 §2). <see cref="Skipped"/> is not a real difference — it flags an
/// entity the pipeline refuses to touch (e.g. an ambiguous data-type name), carrying a
/// <see cref="SchemaEntityChange.Note"/> explaining why.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchemaChangeKind>))]
public enum SchemaChangeKind
{
    /// <summary>Present in the snapshot, absent from the live instance — will be created.</summary>
    Added,

    /// <summary>Matched in both but the bodies differ — will be updated.</summary>
    Changed,

    /// <summary>Present live, matched by nothing in the snapshot — a prune candidate (deleted only with <c>--prune</c>).</summary>
    Removed,

    /// <summary>Matched but not safely actionable (e.g. ambiguous identity); left untouched.</summary>
    Skipped,
}

/// <summary>
/// One entity's place in a schema diff: what kind of entity, how it differs, its human
/// identity, and the ids on each side. Serialized (lean) for the <c>diff</c> command's output;
/// the <see cref="DesiredBody"/> it also carries (but does not serialize) is what
/// <c>apply</c> sends.
/// </summary>
/// <param name="Kind">The entity kind: one of the <see cref="SchemaKinds"/> tags.</param>
/// <param name="Change">How the entity differs.</param>
/// <param name="Identity">
/// The human identity: the alias for types, templates and user groups, the name for data types,
/// dictionary items and member groups, and the ISO code for languages.
/// </param>
/// <param name="DesiredId">The entity id in the snapshot, or null for a <see cref="SchemaChangeKind.Removed"/> entity.</param>
/// <param name="CurrentId">The entity id in the live instance, or null for an <see cref="SchemaChangeKind.Added"/> entity.</param>
/// <param name="IdMismatch">
/// True when the entity was matched by its human key (alias/name) rather than its id, because
/// the ids differ across the two sides — apply updates the *live* entity's id, not the
/// snapshot's. Informational.
/// </param>
/// <param name="Note">A human explanation for a <see cref="SchemaChangeKind.Skipped"/> entity, else null.</param>
/// <remarks>
/// Serialized as it is (#229): <c>idMismatch</c> is a boolean, and an absent id or note is an
/// explicit null rather than <c>""</c>, so every row has the same fields.
/// </remarks>
public sealed record SchemaEntityChange(
    string Kind,
    SchemaChangeKind Change,
    string Identity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? DesiredId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? CurrentId,
    bool IdMismatch = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Note = null
)
{
    /// <summary>
    /// For a changed entity, the paths at which the snapshot body differs from the live one
    /// (<see cref="JsonPathDiff"/>, #229), e.g. <c>properties.title.validation.mandatory</c>. Null
    /// for an added, removed or skipped entity. Always serialized.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<string>? Changes { get; init; }

    /// <summary>
    /// The snapshot body to create/update with. Not serialized into the <c>diff</c> output
    /// (bodies would bloat it) — it exists so <c>apply</c> can derive its writes from the same
    /// diff the user reviewed. Null for <see cref="SchemaChangeKind.Removed"/> /
    /// <see cref="SchemaChangeKind.Skipped"/>.
    /// </summary>
    [JsonIgnore]
    public JsonNode? DesiredBody { get; init; }

    /// <summary>
    /// The live body, for a changed or removed entity (#227): a dictionary update moves the item
    /// when its parent differs, and prune orders dictionary and language deletes by what they
    /// point at. Not serialized. Null for an added or skipped entity.
    /// </summary>
    [JsonIgnore]
    public JsonNode? CurrentBody { get; init; }
}

/// <summary>The diff for one entity kind, grouped by change. Unchanged entities are counted, not listed, to keep output lean.</summary>
/// <param name="Added">Entities to create.</param>
/// <param name="Changed">Entities to update.</param>
/// <param name="Removed">Live entities the snapshot does not contain (prune candidates).</param>
/// <param name="Skipped">Entities left untouched with a reason.</param>
/// <param name="Unchanged">Count of entities identical on both sides.</param>
public sealed record SchemaKindDiff(
    IReadOnlyList<SchemaEntityChange> Added,
    IReadOnlyList<SchemaEntityChange> Changed,
    IReadOnlyList<SchemaEntityChange> Removed,
    IReadOnlyList<SchemaEntityChange> Skipped,
    int Unchanged
)
{
    /// <summary>Whether this kind has any actionable difference (create/update/delete).</summary>
    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;

    /// <summary>
    /// For every matched pair that has an id on both sides, the live id of the snapshot id. A
    /// key match across instances usually pairs different ids, so a reference to a same-kind item
    /// (a dictionary item's parent) is translated through this before it is written. Not
    /// serialized.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<Guid, Guid> LiveIds { get; init; } = new Dictionary<Guid, Guid>();

    /// <summary>A kind with no entities on either side.</summary>
    public static SchemaKindDiff None { get; } = new([], [], [], [], 0);
}

/// <summary>
/// The full schema diff across every entity kind (issue #68, extended by #186). Produced by
/// <see cref="SchemaDiffEngine"/>, rendered by <c>schema diff</c>, and consumed by
/// <c>schema apply</c>.
/// </summary>
/// <param name="DocumentTypes">Document-type differences.</param>
/// <param name="MediaTypes">Media-type differences.</param>
/// <param name="MemberTypes">Member-type differences.</param>
/// <param name="DataTypes">Data-type differences.</param>
/// <param name="Templates">Template differences.</param>
public sealed record SchemaDiff(
    SchemaKindDiff DocumentTypes,
    SchemaKindDiff MediaTypes,
    SchemaKindDiff MemberTypes,
    SchemaKindDiff DataTypes,
    SchemaKindDiff Templates
)
{
    /// <summary>Language differences (#227).</summary>
    public SchemaKindDiff Languages { get; init; } = SchemaKindDiff.None;

    /// <summary>Dictionary item differences (#227).</summary>
    public SchemaKindDiff DictionaryItems { get; init; } = SchemaKindDiff.None;

    /// <summary>Member group differences (#227).</summary>
    public SchemaKindDiff MemberGroups { get; init; } = SchemaKindDiff.None;

    /// <summary>User group differences (#227).</summary>
    public SchemaKindDiff UserGroups { get; init; } = SchemaKindDiff.None;

    /// <summary>
    /// Partial view differences (#292). <see cref="SchemaKindDiff.None"/> when the snapshot has
    /// no <c>partialViews</c> section, so a snapshot that does not manage files never prunes one.
    /// </summary>
    public SchemaKindDiff PartialViews { get; init; } = SchemaKindDiff.None;

    /// <summary>Stylesheet differences (#292); none when the snapshot has no such section.</summary>
    public SchemaKindDiff Stylesheets { get; init; } = SchemaKindDiff.None;

    /// <summary>Script differences (#292); none when the snapshot has no such section.</summary>
    public SchemaKindDiff Scripts { get; init; } = SchemaKindDiff.None;

    /// <summary>Every kind's diff, in the order the diff output lists them.</summary>
    [JsonIgnore]
    public IEnumerable<SchemaKindDiff> Kinds =>
        [
            DocumentTypes,
            MediaTypes,
            MemberTypes,
            DataTypes,
            Templates,
            Languages,
            DictionaryItems,
            MemberGroups,
            UserGroups,
            PartialViews,
            Stylesheets,
            Scripts,
        ];

    /// <summary>Whether any kind has an actionable difference — i.e. apply would do something.</summary>
    [JsonIgnore]
    public bool HasChanges => Kinds.Any(k => k.HasChanges);
}
