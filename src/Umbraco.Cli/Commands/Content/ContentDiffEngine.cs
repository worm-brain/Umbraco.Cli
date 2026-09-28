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
    /// <param name="defaultCulture">
    /// The instance's default language, which picks the variant whose name a row shows (#293);
    /// null falls back to the first variant.
    /// </param>
    /// <returns>The classified differences, in plan order (see <see cref="ContentDiff.Documents"/>).</returns>
    public static ContentDiff Compare(
        ContentSnapshot desired,
        ContentSnapshot current,
        string? defaultCulture = null
    )
    {
        // Index live documents by id (last wins - a well-formed snapshot has unique ids; this is
        // just defensive against a hand-edited one rather than throwing).
        var currentById = new Dictionary<Guid, ContentNode>();
        foreach (var c in current.Documents)
            currentById[c.Id] = c;

        var documents = new List<ContentDocumentChange>();
        var matchedLiveIds = new HashSet<Guid>();
        var unchanged = 0;
        // #291: per read-only property, the documents whose snapshot value apply cannot write.
        var unpromoted = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var d in desired.Documents)
        {
            // Normalised once, here (#224): the body the diff compares is the body apply sends.
            // Both sides are normalised at compare time, not at export, so a snapshot written by
            // an older CLI (verbatim bodies) still compares clean.
            var body = ContentBodyNormaliser.Normalise(d.Body);
            // #293: every row says which document it is, not only its id.
            var name = NameOf(d.Body, defaultCulture);
            var type = DocumentTypeIdOf(d.Body);

            if (!currentById.TryGetValue(d.Id, out var live))
            {
                // Present in the snapshot, absent live -> create it with its snapshot id and parent,
                // then publish what the snapshot has published (#223).
                documents.Add(
                    new ContentDocumentChange(ContentChangeKind.Added, d.Id, d.Parent)
                    {
                        DesiredBody = body,
                        State = ContentPublishState.ForCreate(d.Body),
                        Name = name,
                        DocumentTypeId = type,
                    }
                );
                TallyUnpromoted(unpromoted, d.Body, live: null);
                continue;
            }

            matchedLiveIds.Add(d.Id);
            TallyUnpromoted(unpromoted, d.Body, live.Body);
            // #291: read-only values are left out of the comparison, since apply cannot change
            // them; the body apply sends (DesiredBody) still carries them, as it always did.
            var bodyChanges = JsonPathDiff.Paths(
                ContentBodyNormaliser.ForComparison(d.Body),
                ContentBodyNormaliser.ForComparison(live.Body)
            );
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
                        Name = name,
                        DocumentTypeId = type,
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
                        Name = name,
                        DocumentTypeId = type,
                    }
                );
            }
            else
            {
                unchanged++;
            }
        }

        // Any live document the snapshot never mentioned is a prune candidate, after the rest and
        // in live pre-order. It keeps its type so a prune can exclude whole types (#225), and its
        // name so a prune plan can be reviewed before --yes (#293).
        documents.AddRange(
            current
                .Documents.Where(c => !matchedLiveIds.Contains(c.Id))
                .Select(c => new ContentDocumentChange(ContentChangeKind.Removed, c.Id)
                {
                    DocumentTypeId = DocumentTypeIdOf(c.Body),
                    Name = NameOf(c.Body, defaultCulture),
                })
        );

        return new ContentDiff(documents, unchanged)
        {
            LiveParents = currentById.ToDictionary(kv => kv.Key, kv => kv.Value.Parent),
            UnpromotedValues = unpromoted,
        };
    }

    /// <summary>
    /// A document's name as a row shows it (#293): the invariant variant's, else the default
    /// language's, else the first variant's.
    /// </summary>
    /// <param name="body">The verbatim document body.</param>
    /// <param name="defaultCulture">The instance's default language, or null.</param>
    /// <returns>The name, or null when the body has no named variant.</returns>
    internal static string? NameOf(JsonNode? body, string? defaultCulture)
    {
        var variants = (body?["variants"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var chosen =
            variants.FirstOrDefault(v => ContentBodyNormaliser.Text(v, "culture") is null)
            ?? variants.FirstOrDefault(v =>
                string.Equals(
                    ContentBodyNormaliser.Text(v, "culture"),
                    defaultCulture,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?? variants.FirstOrDefault();
        return ContentBodyNormaliser.Text(chosen, "name");
    }

    /// <summary>
    /// Counts, per read-only property (#291), a snapshot document whose value apply cannot write:
    /// a value on a document apply creates, or one that differs from the live value. Counted once
    /// per document, however many cultures of the property differ.
    /// </summary>
    /// <param name="counts">The per-property document counts to add to.</param>
    /// <param name="desired">The snapshot document's body.</param>
    /// <param name="live">The live document's body, or null when apply creates the document.</param>
    private static void TallyUnpromoted(
        Dictionary<string, int> counts,
        JsonNode desired,
        JsonNode? live
    )
    {
        var liveValues = ContentBodyNormaliser.ReadOnlyValues(live).ToList();
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in ContentBodyNormaliser.ReadOnlyValues(desired))
        {
            // A snapshot with no value for the property has nothing to promote.
            if (
                value["value"] is null
                || ContentBodyNormaliser.Text(value, "alias") is not { } alias
            )
                continue;
            var match = liveValues.FirstOrDefault(l => SameSlot(l, value));
            if (!JsonNode.DeepEquals(value["value"], match?["value"]))
                aliases.Add(alias);
        }
        foreach (var alias in aliases)
            counts[alias] = counts.GetValueOrDefault(alias) + 1;
    }

    /// <summary>Whether two values are the same property in the same culture and segment.</summary>
    /// <param name="a">A value object.</param>
    /// <param name="b">Another value object.</param>
    /// <returns>True when alias, culture and segment all match.</returns>
    private static bool SameSlot(JsonNode a, JsonNode b) =>
        ContentBodyNormaliser.Text(a, "alias") == ContentBodyNormaliser.Text(b, "alias")
        && ContentBodyNormaliser.Text(a, "culture") == ContentBodyNormaliser.Text(b, "culture")
        && ContentBodyNormaliser.Text(a, "segment") == ContentBodyNormaliser.Text(b, "segment");

    /// <summary>Reads <c>documentType.id</c> from a document body, or null when it is absent.</summary>
    /// <param name="body">The verbatim document body.</param>
    /// <returns>The document type id, or null.</returns>
    private static Guid? DocumentTypeIdOf(JsonNode? body) =>
        Guid.TryParse(body?["documentType"]?["id"]?.GetValue<string>(), out var id) ? id : null;
}
