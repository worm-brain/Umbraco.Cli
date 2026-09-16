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
        var drifted = new List<ContentDocumentChange>();
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
            var bodyDiffers = !JsonNode.DeepEquals(d.Body, live.Body);
            var parentDiffers = d.Parent != live.Parent;

            if (bodyDiffers)
            {
                // Body differs -> update (converges the body). If the parent also drifted, that part
                // is still not fixed - apply does not move documents - but the update is real work.
                changed.Add(
                    new ContentDocumentChange(ContentChangeKind.Changed, d.Id, d.Parent)
                    {
                        DesiredBody = d.Body,
                    }
                );
            }
            else if (parentDiffers)
            {
                // Body identical, only placement differs. Advisory: reported but never applied (an
                // update would be a no-op that leaves the drift, so it must not enter the plan).
                drifted.Add(new ContentDocumentChange(ContentChangeKind.Drifted, d.Id, d.Parent));
            }
            else
            {
                unchanged++;
            }
        }

        // Any live document the snapshot never mentioned is a prune candidate.
        var removed = current
            .Documents.Where(c => !matchedLiveIds.Contains(c.Id))
            .Select(c => new ContentDocumentChange(ContentChangeKind.Removed, c.Id))
            .ToList();

        return new ContentDiff(added, changed, removed, drifted, unchanged);
    }
}
