using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Content;

/// <summary>How a document in the desired snapshot relates to the live instance. Serialized by name (#229).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentChangeKind>))]
public enum ContentChangeKind
{
    /// <summary>In the snapshot, absent live - apply creates it (with its snapshot id and parent).</summary>
    Added,

    /// <summary>
    /// Present in both but the body or the publish state differs - apply updates the live document
    /// and/or publishes and unpublishes its cultures.
    /// </summary>
    Changed,

    /// <summary>Live but absent from the snapshot - apply deletes it, but only under <c>--prune</c>.</summary>
    Removed,

    /// <summary>
    /// Present in both with an identical body and publish state but a different parent. This is <b>advisory only</b>:
    /// apply replaces bodies, it does not move documents, so a drift is reported by <c>diff</c> but
    /// never generates an apply step (which is why it is separate from <see cref="Changed"/> - an
    /// update here would be a no-op that could never converge the drift).
    /// </summary>
    Drifted,
}

/// <summary>
/// One document-level difference between a snapshot and a live instance (issue #100), with what
/// apply needs to act on it. Documents are matched by GUID only - unlike schema entities they have
/// no stable natural key (a name is per-culture and not unique), so identity is the document id and
/// portability depends on the id being preserved across environments. What the <c>diff</c> command
/// prints is the <see cref="ToRow"/> projection.
/// </summary>
/// <param name="Change">The kind of change.</param>
/// <param name="Id">The document id the change targets.</param>
/// <param name="Parent">The desired parent id; null at the content root and for a removed document.</param>
public sealed record ContentDocumentChange(ContentChangeKind Change, Guid Id, Guid? Parent = null)
{
    /// <summary>What differs, for the row's <c>changes</c> (see <see cref="ContentDiffRow.Changes"/>).</summary>
    public IReadOnlyList<string>? Changes { get; init; }

    /// <summary>
    /// For an added or changed document, the <b>normalised</b> snapshot body (#224) - exactly what
    /// the diff compared, so apply sends it as it is.
    /// </summary>
    public JsonNode? DesiredBody { get; init; }

    /// <summary>
    /// For a changed document, whether the body differs. False means only the publish state does
    /// (#223), so apply publishes or unpublishes without an update. Not used for other kinds: an
    /// added document is always created.
    /// </summary>
    public bool BodyChanged { get; init; }

    /// <summary>
    /// For an added or changed document, what apply publishes and unpublishes so the target matches
    /// the snapshot's publish state (#223).
    /// </summary>
    public ContentPublishState.Steps State { get; init; } = ContentPublishState.Steps.None;

    /// <summary>
    /// For a removed document, its live document type id, so a prune can leave whole types alone
    /// (<c>--exclude-type</c>, #225).
    /// </summary>
    public Guid? DocumentTypeId { get; init; }

    /// <summary>The change as the <c>diff</c> command reports it.</summary>
    /// <returns>The row.</returns>
    public ContentDiffRow ToRow() => new(Change, Id, Parent, Changes);
}

/// <summary>
/// One row of <c>content diff</c> output (#229): the change record serialized as it is, so empty
/// fields are real nulls rather than <c>""</c>, and every row has the same fields.
/// </summary>
/// <param name="Change">The kind of change.</param>
/// <param name="Id">The document id.</param>
/// <param name="Parent">The desired parent id; null at the content root and for a removed document.</param>
/// <param name="Changes">
/// What differs, as paths into the normalised body (<see cref="JsonPathDiff"/>), plus
/// <c>state[culture]</c> (<c>state</c> for an invariant document) for each publish or unpublish,
/// and <c>parent</c> for a drift. Null for an added or removed document, where the whole document
/// is the change.
/// </param>
public sealed record ContentDiffRow(
    ContentChangeKind Change,
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? Parent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IReadOnlyList<string>? Changes
);

/// <summary>
/// The full content diff (issue #100): every document that differs, plus a count of unchanged ones.
/// <see cref="Documents"/> is in the snapshot's pre-order (parents before children) with the removed
/// documents after, in live pre-order - the order apply creates, publishes and prunes in - and
/// the per-kind lists are views of it. Mirrors the schema diff's shape but for a single kind.
/// </summary>
/// <param name="Documents">Every differing document, in plan order.</param>
/// <param name="Unchanged">Count of documents identical in snapshot and live.</param>
public sealed record ContentDiff(IReadOnlyList<ContentDocumentChange> Documents, int Unchanged)
{
    /// <summary>Documents to create.</summary>
    public IReadOnlyList<ContentDocumentChange> Added => Of(ContentChangeKind.Added);

    /// <summary>Documents to update and/or (un)publish.</summary>
    public IReadOnlyList<ContentDocumentChange> Changed => Of(ContentChangeKind.Changed);

    /// <summary>Documents a prune would delete.</summary>
    public IReadOnlyList<ContentDocumentChange> Removed => Of(ContentChangeKind.Removed);

    /// <summary>Documents whose body and state match but whose parent differs (not applied).</summary>
    public IReadOnlyList<ContentDocumentChange> Drifted => Of(ContentChangeKind.Drifted);

    /// <summary>
    /// True when applying would create, change, or remove at least one document. Drift does not
    /// count - apply cannot act on it, so a diff that is only drift is "no changes to apply".
    /// </summary>
    public bool HasChanges => Documents.Any(d => d.Change is not ContentChangeKind.Drifted);

    /// <summary>The <c>diff</c> command's rows: added, changed, removed, then drifted.</summary>
    public IReadOnlyList<ContentDiffRow> Rows =>
        [.. Added.Concat(Changed).Concat(Removed).Concat(Drifted).Select(d => d.ToRow())];

    /// <summary>
    /// Every live document's parent (null at the root), so a prune can tell whether a removed
    /// document sits under an excluded subtree through ancestors the snapshot does contain
    /// (<c>--exclude-root</c>, #225).
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid?> LiveParents { get; init; } =
        new Dictionary<Guid, Guid?>();

    private IReadOnlyList<ContentDocumentChange> Of(ContentChangeKind kind) =>
        [.. Documents.Where(d => d.Change == kind)];
}
