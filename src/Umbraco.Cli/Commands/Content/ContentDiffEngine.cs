using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The pure, client-free core of the content pipeline (issue #100 / ADR 0006): given a desired
/// snapshot and the current live snapshot, classify each document as added, changed, removed, or
/// unchanged. Matching is by document GUID only - documents have no stable natural key, so cross-
/// environment identity depends on the id being preserved (which apply does on create).
/// </summary>
public static class ContentDiffEngine
{
    /// <summary>
    /// Compares a desired snapshot against the current live snapshot.
    /// </summary>
    /// <param name="desired">The snapshot to converge the instance towards.</param>
    /// <param name="current">A snapshot of the instance's current content.</param>
    /// <returns>The classified differences.</returns>
    public static ContentDiff Compare(ContentSnapshot desired, ContentSnapshot current)
    {
        // Index live documents by id (last wins - a well-formed snapshot has unique ids; this is
        // just defensive against a hand-edited one rather than throwing).
        var currentById = new Dictionary<Guid, ContentNode>();
        foreach (var c in current.Documents)
            currentById[c.Id] = c;

        var added = new List<ContentDocumentChange>();
        var changed = new List<ContentDocumentChange>();
        var matchedLiveIds = new HashSet<Guid>();
        var unchanged = 0;

        foreach (var d in desired.Documents)
        {
            if (!currentById.TryGetValue(d.Id, out var live))
            {
                // Present in the snapshot, absent live -> create it with its snapshot id and parent.
                added.Add(
                    new ContentDocumentChange(ContentChangeKind.Added, d.Id, d.Parent)
                    {
                        DesiredBody = d.Body,
                    }
                );
                continue;
            }

            matchedLiveIds.Add(d.Id);
            var parentDrift = d.Parent != live.Parent;
            if (JsonNode.DeepEquals(d.Body, live.Body) && !parentDrift)
            {
                unchanged++;
                continue;
            }

            // Body (or placement) differs -> update. Placement drift is flagged for visibility but
            // not fixed: apply replaces the document body, it does not move documents (issue #100).
            changed.Add(
                new ContentDocumentChange(ContentChangeKind.Changed, d.Id, d.Parent, parentDrift)
                {
                    DesiredBody = d.Body,
                }
            );
        }

        // Any live document the snapshot never mentioned is a prune candidate.
        var removed = current
            .Documents.Where(c => !matchedLiveIds.Contains(c.Id))
            .Select(c => new ContentDocumentChange(ContentChangeKind.Removed, c.Id))
            .ToList();

        return new ContentDiff(added, changed, removed, unchanged);
    }
}
