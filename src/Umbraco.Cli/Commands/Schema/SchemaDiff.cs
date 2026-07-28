using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// How a single schema entity differs between the desired snapshot and the live instance
/// (issue #68 / ADR 0004 §2). <see cref="Skipped"/> is not a real difference — it flags an
/// entity the pipeline refuses to touch (e.g. an ambiguous data-type name), carrying a
/// <see cref="SchemaEntityChange.Note"/> explaining why.
/// </summary>
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
/// <param name="Kind">The entity kind: <c>documentType</c>, <c>dataType</c>, or <c>template</c>.</param>
/// <param name="Change">How the entity differs.</param>
/// <param name="Identity">The human identity (alias for doc types/templates, name for data types).</param>
/// <param name="DesiredId">The entity id in the snapshot, or null for a <see cref="SchemaChangeKind.Removed"/> entity.</param>
/// <param name="CurrentId">The entity id in the live instance, or null for an <see cref="SchemaChangeKind.Added"/> entity.</param>
/// <param name="IdMismatch">
/// True when the entity was matched by its human key (alias/name) rather than its id, because
/// the ids differ across the two sides — apply updates the *live* entity's id, not the
/// snapshot's. Informational.
/// </param>
/// <param name="Note">A human explanation for a <see cref="SchemaChangeKind.Skipped"/> entity, else null.</param>
public sealed record SchemaEntityChange(
    string Kind,
    SchemaChangeKind Change,
    string Identity,
    Guid? DesiredId,
    Guid? CurrentId,
    bool IdMismatch = false,
    string? Note = null
)
{
    /// <summary>
    /// The snapshot body to create/update with. Not serialized into the <c>diff</c> output
    /// (bodies would bloat it) — it exists so <c>apply</c> can derive its writes from the same
    /// diff the user reviewed. Null for <see cref="SchemaChangeKind.Removed"/> /
    /// <see cref="SchemaChangeKind.Skipped"/>.
    /// </summary>
    [JsonIgnore]
    public JsonNode? DesiredBody { get; init; }
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
}

/// <summary>
/// The full schema diff across all three entity kinds (issue #68). Produced by
/// <see cref="SchemaDiffEngine"/>, rendered by <c>schema diff</c>, and consumed by
/// <c>schema apply</c>.
/// </summary>
/// <param name="DocumentTypes">Document-type differences.</param>
/// <param name="DataTypes">Data-type differences.</param>
/// <param name="Templates">Template differences.</param>
public sealed record SchemaDiff(
    SchemaKindDiff DocumentTypes,
    SchemaKindDiff DataTypes,
    SchemaKindDiff Templates
)
{
    /// <summary>Whether any kind has an actionable difference — i.e. apply would do something.</summary>
    [JsonIgnore]
    public bool HasChanges =>
        DocumentTypes.HasChanges || DataTypes.HasChanges || Templates.HasChanges;
}
