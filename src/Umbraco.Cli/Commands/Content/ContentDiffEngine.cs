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
    /// <returns>The classified differences, in plan order (see <see cref="ContentDiff.Documents"/>).</returns>
    public static ContentDiff Compare(ContentSnapshot desired, ContentSnapshot current)
    {
        // Index live documents by id (last wins - a well-formed snapshot has unique ids; this is
        // just defensive against a hand-edited one rather than throwing).
        var currentById = new Dictionary<Guid, ContentNode>();
        foreach (var c in current.Documents)
            currentById[c.Id] = c;

        var documents = new List<ContentDocumentChange>();
        var matchedLiveIds = new HashSet<Guid>();
        var unchanged = 0;

        foreach (var d in desired.Documents)
        {
            // Normalised once, here (#224): the body the diff compares is the body apply sends.
            // Both sides are normalised at compare time, not at export, so a snapshot written by
            // an older CLI (verbatim bodies) still compares clean.
            var body = ContentBodyNormaliser.Normalise(d.Body);

            if (!currentById.TryGetValue(d.Id, out var live))
            {
                // Present in the snapshot, absent live -> create it with its snapshot id and parent,
                // then publish what the snapshot has published (#223).
                documents.Add(
                    new ContentDocumentChange(ContentChangeKind.Added, d.Id, d.Parent)
                    {
                        DesiredBody = body,
                        State = ContentPublishState.ForCreate(d.Body),
                    }
                );
                continue;
            }

            matchedLiveIds.Add(d.Id);
            var bodyChanges = JsonPathDiff.Paths(body, ContentBodyNormaliser.Normalise(live.Body));
            var bodyDiffers = bodyChanges.Count > 0;
            // State is read from the verbatim bodies (the normaliser drops it), so a state-only
            // difference is a publish step with no update (#223).
            var state = ContentPublishState.ForMatch(d.Body, live.Body, bodyDiffers);

            if (bodyDiffers || !state.IsEmpty)
            {
                // Body or state differs -> update and/or (un)publish. If the parent also drifted,
                // that part is still not fixed - apply does not move documents - but the rest is
                // real work.
                documents.Add(
                    new ContentDocumentChange(ContentChangeKind.Changed, d.Id, d.Parent)
                    {
                        DesiredBody = body,
                        BodyChanged = bodyDiffers,
                        State = state,
                        Changes =
                        [
                            .. bodyChanges,
                            .. state.Publish?.StatePaths() ?? [],
                            .. state.Unpublish?.StatePaths() ?? [],
                        ],
                    }
                );
            }
            else if (d.Parent != live.Parent)
            {
                // Body identical, only placement differs. Advisory: reported but never applied (an
                // update would be a no-op that leaves the drift, so it must not enter the plan).
                documents.Add(
                    new ContentDocumentChange(ContentChangeKind.Drifted, d.Id, d.Parent)
                    {
                        Changes = ["parent"],
                    }
                );
            }
            else
            {
                unchanged++;
            }
        }

        // Any live document the snapshot never mentioned is a prune candidate, after the rest and
        // in live pre-order. It keeps its type so a prune can exclude whole types (#225).
        documents.AddRange(
            current
                .Documents.Where(c => !matchedLiveIds.Contains(c.Id))
                .Select(c => new ContentDocumentChange(ContentChangeKind.Removed, c.Id)
                {
                    DocumentTypeId = DocumentTypeIdOf(c.Body),
                })
        );

        return new ContentDiff(documents, unchanged)
        {
            LiveParents = currentById.ToDictionary(kv => kv.Key, kv => kv.Value.Parent),
        };
    }

    /// <summary>Reads <c>documentType.id</c> from a document body, or null when it is absent.</summary>
    /// <param name="body">The verbatim document body.</param>
    /// <returns>The document type id, or null.</returns>
    private static Guid? DocumentTypeIdOf(JsonNode? body) =>
        Guid.TryParse(body?["documentType"]?["id"]?.GetValue<string>(), out var id) ? id : null;
}
