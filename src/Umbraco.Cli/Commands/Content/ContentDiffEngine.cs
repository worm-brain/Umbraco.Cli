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
        // #291: read-only values are left out of the comparison (ForComparison), since apply
        // cannot change them; the body apply sends (DesiredBody) still carries them. State is read
        // from the verbatim bodies (the normaliser drops it), so a state-only difference is a
        // publish step with no update (#223). Every non-empty step list yields at least one
        // state path, so "no state paths" is exactly Steps.IsEmpty.
        var tree = SnapshotTreeDiff.Classify(
            desired.Documents,
            current.Documents,
            ContentBodyNormaliser.ForComparison,
            (d, live, bodyDiffers) =>
            {
                var state = ContentPublishState.ForMatch(d.Body, live.Body, bodyDiffers);
                return new ExtraComparison<ContentPublishState.Steps>(
                    state,
                    [.. state.Publish?.StatePaths() ?? [], .. state.Unpublish?.StatePaths() ?? []]
                );
            }
        );

        // #291: per read-only property, the documents whose snapshot value apply cannot write.
        // Tallied in snapshot order, as the per-property warnings are listed in that order.
        var unpromoted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in desired.Documents)
            TallyUnpromoted(unpromoted, d.Body, tree.LiveById.GetValueOrDefault(d.Id)?.Body);

        return new ContentDiff(
            [.. tree.Entries.Select(e => ToChange(e, defaultCulture))],
            tree.Unchanged
        )
        {
            LiveParents = tree.LiveParents(),
            UnpromotedValues = unpromoted,
        };
    }

    /// <summary>
    /// Turns a classified entry into the content change record: every row says which document it
    /// is (#293), and an added or changed document carries its normalised body (#224) and the
    /// publish steps apply takes (#223).
    /// </summary>
    /// <param name="entry">The classified entry.</param>
    /// <param name="defaultCulture">The instance's default language, or null.</param>
    /// <returns>The change record.</returns>
    private static ContentDocumentChange ToChange(
        TreeEntry<ContentNode, ContentPublishState.Steps> entry,
        string? defaultCulture
    )
    {
        var node = entry.Node;
        var name = NameOf(node.Body, defaultCulture);
        var type = DocumentTypeIdOf(node.Body);
        return entry.Kind switch
        {
            // Present in the snapshot, absent live -> create it with its snapshot id and parent,
            // then publish what the snapshot has published (#223). Normalised here (#224): the
            // body the diff compares is the body apply sends.
            TreeChangeKind.Added => new(TreeChangeKind.Added, node.Id, node.Parent)
            {
                DesiredBody = ContentBodyNormaliser.Normalise(node.Body),
                State = ContentPublishState.ForCreate(node.Body),
                Name = name,
                DocumentTypeId = type,
            },
            TreeChangeKind.Changed => new(TreeChangeKind.Changed, node.Id, node.Parent)
            {
                DesiredBody = ContentBodyNormaliser.Normalise(node.Body),
                BodyChanged = entry.BodyChanged,
                State = entry.Extra ?? ContentPublishState.Steps.None,
                Name = name,
                DocumentTypeId = type,
                Changes = entry.Changes,
            },
            TreeChangeKind.Drifted => new(TreeChangeKind.Drifted, node.Id, node.Parent)
            {
                Changes = entry.Changes,
                Name = name,
                DocumentTypeId = type,
            },
            // A live document the snapshot never mentioned keeps its live type so a prune can
            // exclude whole types (#225), and its name so a prune plan can be reviewed (#293).
            _ => new(TreeChangeKind.Removed, node.Id) { DocumentTypeId = type, Name = name },
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
            variants.FirstOrDefault(v => SnapshotBody.Text(v, "culture") is null)
            ?? variants.FirstOrDefault(v =>
                string.Equals(
                    SnapshotBody.Text(v, "culture"),
                    defaultCulture,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?? variants.FirstOrDefault();
        return SnapshotBody.Text(chosen, "name");
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
            if (value["value"] is null || SnapshotBody.Text(value, "alias") is not { } alias)
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
        SnapshotBody.Text(a, "alias") == SnapshotBody.Text(b, "alias")
        && SnapshotBody.Text(a, "culture") == SnapshotBody.Text(b, "culture")
        && SnapshotBody.Text(a, "segment") == SnapshotBody.Text(b, "segment");

    /// <summary>Reads <c>documentType.id</c> from a document body, or null when it is absent.</summary>
    /// <param name="body">The verbatim document body.</param>
    /// <returns>The document type id, or null.</returns>
    private static Guid? DocumentTypeIdOf(JsonNode? body) =>
        Guid.TryParse(body?["documentType"]?["id"]?.GetValue<string>(), out var id) ? id : null;
}
