using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Content;

/// <summary>How a document in the desired snapshot relates to the live instance.</summary>
public enum ContentChangeKind
{
    /// <summary>In the snapshot, absent live - apply creates it (with its snapshot id and parent).</summary>
    Added,

    /// <summary>Present in both but the body differs - apply updates the live document.</summary>
    Changed,

    /// <summary>Live but absent from the snapshot - apply deletes it, but only under <c>--prune</c>.</summary>
    Removed,

    /// <summary>
    /// Present in both with an identical body but a different parent. This is <b>advisory only</b>:
    /// apply replaces bodies, it does not move documents, so a drift is reported by <c>diff</c> but
    /// never generates an apply step (which is why it is separate from <see cref="Changed"/> - an
    /// update here would be a no-op that could never converge the drift).
    /// </summary>
    Drifted,
}

/// <summary>
/// One document-level difference between a snapshot and a live instance (issue #100). Documents
/// are matched by GUID only - unlike schema entities they have no stable natural key (a name is
/// per-culture and not unique), so identity is the document id and portability depends on the id
/// being preserved across environments.
/// </summary>
/// <param name="Change">The kind of change.</param>
/// <param name="Id">The document id the change targets.</param>
/// <param name="Parent">The desired parent id (for a create); null at the content root.</param>
public sealed record ContentDocumentChange(ContentChangeKind Change, Guid Id, Guid? Parent = null)
{
    /// <summary>
    /// The desired document body carried through to apply for a create/update. Not serialized into
    /// the diff output (which is a summary), only used by <see cref="ContentApplier"/>.
    /// </summary>
    [JsonIgnore]
    public JsonNode? DesiredBody { get; init; }
}

/// <summary>
/// The full content diff: documents to add, change, or remove (the actionable changes), documents
/// whose placement has drifted (advisory), plus a count of unchanged ones (issue #100). Mirrors the
/// schema diff's shape but for a single kind (documents).
/// </summary>
/// <param name="Added">Documents to create.</param>
/// <param name="Changed">Documents to update (body differs).</param>
/// <param name="Removed">Documents that would be pruned.</param>
/// <param name="Drifted">Documents whose body matches but whose parent differs (not applied).</param>
/// <param name="Unchanged">Count of documents identical in snapshot and live.</param>
public sealed record ContentDiff(
    IReadOnlyList<ContentDocumentChange> Added,
    IReadOnlyList<ContentDocumentChange> Changed,
    IReadOnlyList<ContentDocumentChange> Removed,
    IReadOnlyList<ContentDocumentChange> Drifted,
    int Unchanged
)
{
    /// <summary>
    /// True when applying would create, change, or remove at least one document. Drift does not
    /// count - apply cannot act on it, so a diff that is only drift is "no changes to apply".
    /// </summary>
    public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;
}
