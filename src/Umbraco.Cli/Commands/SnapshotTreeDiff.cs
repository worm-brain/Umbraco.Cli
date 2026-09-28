using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands;

/// <summary>
/// How an item in a tree-shaped snapshot (content, ADR 0006; media, ADR 0008) relates to the live
/// instance. Serialized by name (#229), so the <c>change</c> field of a diff row reads
/// <c>Added</c>, <c>Changed</c>, <c>Removed</c> or <c>Drifted</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TreeChangeKind>))]
public enum TreeChangeKind
{
    /// <summary>In the snapshot, absent live - apply creates it (with its snapshot id and parent).</summary>
    Added,

    /// <summary>
    /// Present in both but the body or the pipeline's extra comparison (a document's publish
    /// state, a media item's file) differs - apply updates the live item.
    /// </summary>
    Changed,

    /// <summary>Live but absent from the snapshot - apply removes it, but only under <c>--prune</c>.</summary>
    Removed,

    /// <summary>
    /// Present in both with nothing different but the parent. This is <b>advisory only</b>: apply
    /// replaces bodies, it does not move items, so a drift is reported by <c>diff</c> but never
    /// generates an apply step (which is why it is separate from <see cref="Changed"/> - an update
    /// here would be a no-op that could never converge the drift).
    /// </summary>
    Drifted,
}

/// <summary>An item of a tree-shaped snapshot: its id, its placement and its verbatim body.</summary>
public interface ISnapshotTreeNode
{
    /// <summary>The item's GUID, which is what matches it across instances.</summary>
    Guid Id { get; }

    /// <summary>The parent item's id, or null at the root.</summary>
    Guid? Parent { get; }

    /// <summary>The verbatim Management API body.</summary>
    JsonNode Body { get; }
}

/// <summary>
/// The outcome of a pipeline's extra comparison for an item on both sides (#274): whatever the
/// pipeline needs to act on it, plus the <c>changes</c> entries it adds.
/// </summary>
/// <typeparam name="TExtra">The pipeline's own result type.</typeparam>
/// <param name="Value">The pipeline's result, carried to <see cref="TreeEntry{TNode,TExtra}.Extra"/>.</param>
/// <param name="Changes">
/// The <c>changes</c> entries for what differs, appended after the body's paths. Empty exactly when
/// the extra comparison finds no difference.
/// </param>
public sealed record ExtraComparison<TExtra>(TExtra Value, IReadOnlyList<string> Changes);

/// <summary>One classified item of a <see cref="TreeDiff{TNode,TExtra}"/>.</summary>
/// <typeparam name="TNode">The snapshot's node type.</typeparam>
/// <typeparam name="TExtra">The pipeline's extra comparison result.</typeparam>
/// <param name="Kind">How the item differs.</param>
/// <param name="Node">The snapshot item, or the live item for <see cref="TreeChangeKind.Removed"/>.</param>
public sealed record TreeEntry<TNode, TExtra>(TreeChangeKind Kind, TNode Node)
    where TNode : ISnapshotTreeNode
{
    /// <summary>For a changed or drifted item, the live item it matched; otherwise default.</summary>
    public TNode? Live { get; init; }

    /// <summary>
    /// For a changed item, the body's paths then the extra comparison's entries; <c>["parent"]</c>
    /// for a drifted item; null for an added or removed item, where the whole item is the change.
    /// </summary>
    public IReadOnlyList<string>? Changes { get; init; }

    /// <summary>For a changed item, whether the normalised bodies differ.</summary>
    public bool BodyChanged { get; init; }

    /// <summary>For a changed item, the extra comparison's value; otherwise default.</summary>
    public TExtra? Extra { get; init; }
}

/// <summary>The classified tree diff that each pipeline turns into its own change records.</summary>
/// <typeparam name="TNode">The snapshot's node type.</typeparam>
/// <typeparam name="TExtra">The pipeline's extra comparison result.</typeparam>
/// <param name="Entries">
/// Every differing item: the snapshot's items in snapshot order (pre-order, so parents first), then
/// the removed items in live order.
/// </param>
/// <param name="Unchanged">How many items are the same on both sides.</param>
/// <param name="LiveById">Every live item by id (last wins on a duplicate id).</param>
public sealed record TreeDiff<TNode, TExtra>(
    IReadOnlyList<TreeEntry<TNode, TExtra>> Entries,
    int Unchanged,
    IReadOnlyDictionary<Guid, TNode> LiveById
)
    where TNode : ISnapshotTreeNode
{
    /// <summary>Every live item's parent (null at the root), so a prune can see what a removal takes.</summary>
    /// <returns>A new dictionary of id to parent id.</returns>
    public Dictionary<Guid, Guid?> LiveParents() =>
        LiveById.ToDictionary(kv => kv.Key, kv => kv.Value.Parent);
}

/// <summary>
/// The classify loop shared by the content and media pipelines (#274). Items match by GUID only;
/// each pipeline supplies how a body is normalised for comparison and one extra comparison (publish
/// state for content, the file for media), then maps the entries to its own change records.
/// </summary>
public static class SnapshotTreeDiff
{
    /// <summary>
    /// Classifies each snapshot item against the live items as added, changed, drifted or
    /// unchanged, and each live item the snapshot does not name as removed.
    /// </summary>
    /// <typeparam name="TNode">The snapshot's node type.</typeparam>
    /// <typeparam name="TExtra">The pipeline's extra comparison result.</typeparam>
    /// <param name="desired">The snapshot's items, in snapshot order.</param>
    /// <param name="live">The live items, in live order.</param>
    /// <param name="normalise">
    /// Reduces a verbatim body to what the comparison sees; applied to both sides. It must not
    /// mutate its input.
    /// </param>
    /// <param name="compareExtra">
    /// The pipeline's extra comparison for an item on both sides: the snapshot item, the live
    /// item, and whether the bodies differ (a document's publish steps depend on it).
    /// </param>
    /// <returns>The classified entries, the unchanged count and the live items by id.</returns>
    public static TreeDiff<TNode, TExtra> Classify<TNode, TExtra>(
        IEnumerable<TNode> desired,
        IReadOnlyList<TNode> live,
        Func<JsonNode, JsonNode> normalise,
        Func<TNode, TNode, bool, ExtraComparison<TExtra>> compareExtra
    )
        where TNode : ISnapshotTreeNode
    {
        // Index live items by id (last wins - a well-formed snapshot has unique ids; this is just
        // defensive against a hand-edited one rather than throwing).
        var liveById = new Dictionary<Guid, TNode>();
        foreach (var l in live)
            liveById[l.Id] = l;

        var entries = new List<TreeEntry<TNode, TExtra>>();
        var matched = new HashSet<Guid>();
        var unchanged = 0;

        foreach (var d in desired)
        {
            if (!liveById.TryGetValue(d.Id, out var current))
            {
                entries.Add(new(TreeChangeKind.Added, d));
                continue;
            }

            matched.Add(d.Id);
            var bodyChanges = JsonPathDiff.Paths(normalise(d.Body), normalise(current.Body));
            var extra = compareExtra(d, current, bodyChanges.Count > 0);

            if (bodyChanges.Count > 0 || extra.Changes.Count > 0)
                // Body or extra differs -> update. If the parent also drifted, that part is still
                // not fixed - apply does not move items - but the rest is real work.
                entries.Add(
                    new(TreeChangeKind.Changed, d)
                    {
                        Live = current,
                        BodyChanged = bodyChanges.Count > 0,
                        Extra = extra.Value,
                        Changes = [.. bodyChanges, .. extra.Changes],
                    }
                );
            else if (d.Parent != current.Parent)
                // Placement only. Advisory: reported but never applied (an update would be a no-op
                // that leaves the drift, so it must not enter the plan).
                entries.Add(
                    new(TreeChangeKind.Drifted, d) { Live = current, Changes = ["parent"] }
                );
            else
                unchanged++;
        }

        // Any live item the snapshot never mentioned is a prune candidate, after the rest and in
        // live order.
        entries.AddRange(
            live.Where(l => !matched.Contains(l.Id))
                .Select(l => new TreeEntry<TNode, TExtra>(TreeChangeKind.Removed, l))
        );

        return new(entries, unchanged, liveById);
    }
}
