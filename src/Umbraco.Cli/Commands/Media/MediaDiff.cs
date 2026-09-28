using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// One media item's difference between a snapshot and the live instance (#226). Items match by
/// GUID only, as documents do: content references media by GUID, so the GUID is what a promotion
/// must keep.
/// </summary>
/// <param name="Change">How it differs. <see cref="TreeChangeKind.Drifted"/> (only the parent differs) is reported, never applied.</param>
/// <param name="Id">The item id.</param>
/// <param name="Parent">The desired parent id; null at the root and for a removed item.</param>
public sealed record MediaItemChange(TreeChangeKind Change, Guid Id, Guid? Parent = null)
{
    /// <summary>What differs: paths into the normalised body, plus <c>file</c> when the file does.</summary>
    public IReadOnlyList<string>? Changes { get; init; }

    /// <summary>For an added or changed item, the normalised snapshot body apply writes.</summary>
    public JsonNode? DesiredBody { get; init; }

    /// <summary>For a changed item, the live item's verbatim body (its file is kept when unchanged).</summary>
    public JsonNode? CurrentBody { get; init; }

    /// <summary>For an added or changed item, the snapshot's file, or null when it holds none.</summary>
    public MediaFile? File { get; init; }

    /// <summary>For a changed item, whether the body (not counting the file) differs.</summary>
    public bool BodyChanged { get; init; }

    /// <summary>For an added or changed item with a file, whether apply must upload it.</summary>
    public bool FileChanged { get; init; }

    /// <summary>The change as <c>media diff</c> reports it.</summary>
    /// <returns>The row.</returns>
    public MediaDiffRow ToRow() => new(Change, Id, Parent, Changes);
}

/// <summary>One row of <c>media diff</c> output; empty fields are real nulls.</summary>
/// <param name="Change">How the item differs.</param>
/// <param name="Id">The item id.</param>
/// <param name="Parent">The desired parent id; null at the root and for a removed item.</param>
/// <param name="Changes">What differs; null for an added or removed item, where the whole item is the change.</param>
public sealed record MediaDiffRow(
    TreeChangeKind Change,
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? Parent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IReadOnlyList<string>? Changes
);

/// <summary>
/// The full media diff (#226): every differing item, in the snapshot's pre-order with the removed
/// items after in live pre-order - the order apply works in.
/// </summary>
/// <param name="Items">Every differing item, in plan order.</param>
/// <param name="Unchanged">How many items are the same on both sides.</param>
public sealed record MediaDiff(IReadOnlyList<MediaItemChange> Items, int Unchanged)
{
    /// <summary>Every live item's parent (null at the root), so a prune can see what a trash would take.</summary>
    public IReadOnlyDictionary<Guid, Guid?> LiveParents { get; init; } =
        new Dictionary<Guid, Guid?>();

    /// <summary>Items a prune would trash.</summary>
    public IReadOnlyList<MediaItemChange> Removed =>
        [.. Items.Where(i => i.Change == TreeChangeKind.Removed)];

    /// <summary>The <c>diff</c> command's rows: added, changed, removed, then drifted.</summary>
    public IReadOnlyList<MediaDiffRow> Rows =>
        [
            .. new[]
            {
                TreeChangeKind.Added,
                TreeChangeKind.Changed,
                TreeChangeKind.Removed,
                TreeChangeKind.Drifted,
            }.SelectMany(kind => Items.Where(i => i.Change == kind).Select(i => i.ToRow())),
        ];
}

/// <summary>
/// The pure core of the media pipeline (#226): classify each item as added, changed, removed,
/// drifted or unchanged. The body comparison runs on <see cref="MediaBody.Normalise"/>d bodies;
/// the file is compared on its own.
/// </summary>
public static class MediaDiffEngine
{
    /// <summary>Compares a snapshot with the live items.</summary>
    /// <param name="desired">The snapshot.</param>
    /// <param name="live">
    /// The live items at the snapshot's root. A live item's <see cref="MediaNode.File"/> carries
    /// what is known of its stored file: its name and size, and its hash when it was downloaded
    /// (<c>--verify-files</c>); an unknown size is -1 and an unknown hash empty.
    /// </param>
    /// <returns>The differences, in plan order.</returns>
    public static MediaDiff Compare(MediaSnapshot desired, IReadOnlyList<MediaNode> live)
    {
        // The file is compared on its own: a snapshot item without a file has nothing to upload,
        // so only one with a file can differ there.
        var tree = SnapshotTreeDiff.Classify(
            desired.Items,
            live,
            MediaBody.Normalise,
            (d, current, _) =>
            {
                var fileChanged = d.File is not null && !SameFile(d.File, current.File);
                return new ExtraComparison<bool>(fileChanged, fileChanged ? ["file"] : []);
            }
        );

        return new MediaDiff([.. tree.Entries.Select(ToChange)], tree.Unchanged)
        {
            LiveParents = tree.LiveParents(),
        };
    }

    /// <summary>
    /// Turns a classified entry into the media change record: an added or changed item carries its
    /// normalised body and its file, and a changed one the live body, whose file apply keeps when
    /// the file is unchanged.
    /// </summary>
    /// <param name="entry">The classified entry; its extra value is whether the file differs.</param>
    /// <returns>The change record.</returns>
    private static MediaItemChange ToChange(TreeEntry<MediaNode, bool> entry)
    {
        var node = entry.Node;
        return entry.Kind switch
        {
            TreeChangeKind.Added => new(TreeChangeKind.Added, node.Id, node.Parent)
            {
                DesiredBody = MediaBody.Normalise(node.Body),
                File = node.File,
                FileChanged = node.File is not null,
            },
            TreeChangeKind.Changed => new(TreeChangeKind.Changed, node.Id, node.Parent)
            {
                DesiredBody = MediaBody.Normalise(node.Body),
                CurrentBody = entry.Live?.Body,
                File = node.File,
                BodyChanged = entry.BodyChanged,
                FileChanged = entry.Extra,
                Changes = entry.Changes,
            },
            // Placement only. Advisory, as for content: apply does not move items.
            TreeChangeKind.Drifted => new(TreeChangeKind.Drifted, node.Id, node.Parent)
            {
                Changes = entry.Changes,
            },
            _ => new(TreeChangeKind.Removed, node.Id),
        };
    }

    /// <summary>
    /// Whether the live file is the snapshot's file: the same name, the same size when the size is
    /// known, and the same hash when the live file was hashed.
    /// </summary>
    /// <param name="desired">The snapshot's file.</param>
    /// <param name="live">What is known of the live file, or null when the item holds none.</param>
    /// <returns>True when nothing known differs.</returns>
    private static bool SameFile(MediaFile desired, MediaFile? live) =>
        live is not null
        && string.Equals(desired.Name, live.Name, StringComparison.Ordinal)
        && (live.Bytes < 0 || live.Bytes == desired.Bytes)
        && (
            live.Sha256.Length == 0
            || string.Equals(desired.Sha256, live.Sha256, StringComparison.OrdinalIgnoreCase)
        );
}
