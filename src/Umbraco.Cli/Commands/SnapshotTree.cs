using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Tree helpers shared by the two tree-shaped snapshot pipelines, content (ADR 0006) and media
/// (ADR 0008): an item's read carries no parent, so a create has it put back, and a prune walks an
/// item's ancestors to see what a cascading delete would take with it.
/// </summary>
public static class SnapshotTree
{
    /// <summary>
    /// Returns a clone of <paramref name="body"/> with its top-level <c>parent</c> set for a create:
    /// <c>{ "id": &lt;parent&gt; }</c>, or <c>null</c> at the root. The original node is not mutated
    /// (it may be shared with the diff output).
    /// </summary>
    /// <param name="body">The snapshot body.</param>
    /// <param name="parent">The parent id, or null for the root.</param>
    /// <returns>A cloned body carrying the parent reference.</returns>
    public static JsonNode WithParent(JsonNode body, Guid? parent)
    {
        var clone = body.DeepClone();
        clone["parent"] = parent is { } p ? new JsonObject { ["id"] = p.ToString() } : null;
        return clone;
    }

    /// <summary>
    /// The removed items with a kept item somewhere under them. Removing one (a delete cascades; a
    /// trash moves the subtree) would take the kept item along, so a prune leaves it in place. A
    /// kept item sits under a removed one when the snapshot places it elsewhere, which apply does
    /// not act on (placement drift).
    /// </summary>
    /// <param name="removed">The items a prune would remove.</param>
    /// <param name="parents">Every live item's parent.</param>
    /// <returns>The removed items to leave alone.</returns>
    public static HashSet<Guid> AncestorsOfKept(
        IReadOnlySet<Guid> removed,
        IReadOnlyDictionary<Guid, Guid?> parents
    )
    {
        var protectedIds = new HashSet<Guid>();
        foreach (var id in parents.Keys.Where(id => !removed.Contains(id)))
        foreach (var ancestor in Lineage(id, parents).Skip(1))
            if (removed.Contains(ancestor))
                protectedIds.Add(ancestor);
        return protectedIds;
    }

    /// <summary>An item id followed by its ancestors' ids, nearest first.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="parents">Every live item's parent.</param>
    /// <returns>The id and its ancestors.</returns>
    public static IEnumerable<Guid> Lineage(Guid id, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        // The guard stops a cycle in a malformed snapshot from looping forever.
        var seen = new HashSet<Guid>();
        for (
            Guid? current = id;
            current is { } c && seen.Add(c);
            current = parents.GetValueOrDefault(c)
        )
            yield return c;
    }
}
