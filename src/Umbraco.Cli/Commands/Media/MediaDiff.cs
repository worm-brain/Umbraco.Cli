using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// One media item's difference between a snapshot and the live instance (#226). Items match by
/// GUID only, as documents do: content references media by GUID, so the GUID is what a promotion
/// must keep.
/// </summary>
/// <param name="Change">How it differs. <see cref="ContentChangeKind.Drifted"/> (only the parent differs) is reported, never applied.</param>
/// <param name="Id">The item id.</param>
/// <param name="Parent">The desired parent id; null at the root and for a removed item.</param>
public sealed record MediaItemChange(ContentChangeKind Change, Guid Id, Guid? Parent = null)
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
    ContentChangeKind Change,
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
        [.. Items.Where(i => i.Change == ContentChangeKind.Removed)];

    /// <summary>The <c>diff</c> command's rows: added, changed, removed, then drifted.</summary>
    public IReadOnlyList<MediaDiffRow> Rows =>
        [
            .. new[]
            {
                ContentChangeKind.Added,
                ContentChangeKind.Changed,
                ContentChangeKind.Removed,
                ContentChangeKind.Drifted,
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
        var liveById = new Dictionary<Guid, MediaNode>();
        foreach (var l in live)
            liveById[l.Id] = l;

        var items = new List<MediaItemChange>();
        var matched = new HashSet<Guid>();
        var unchanged = 0;

        foreach (var d in desired.Items)
        {
            var body = MediaBody.Normalise(d.Body);
            if (!liveById.TryGetValue(d.Id, out var current))
            {
                items.Add(
                    new MediaItemChange(ContentChangeKind.Added, d.Id, d.Parent)
                    {
                        DesiredBody = body,
                        File = d.File,
                        FileChanged = d.File is not null,
                    }
                );
                continue;
            }

            matched.Add(d.Id);
            var bodyChanges = JsonPathDiff.Paths(body, MediaBody.Normalise(current.Body));
            var fileChanged = d.File is not null && !SameFile(d.File, current.File);

            if (bodyChanges.Count > 0 || fileChanged)
                items.Add(
                    new MediaItemChange(ContentChangeKind.Changed, d.Id, d.Parent)
                    {
                        DesiredBody = body,
                        CurrentBody = current.Body,
                        File = d.File,
                        BodyChanged = bodyChanges.Count > 0,
                        FileChanged = fileChanged,
                        Changes = [.. bodyChanges, .. fileChanged ? new[] { "file" } : []],
                    }
                );
            else if (d.Parent != current.Parent)
                // Placement only. Advisory, as for content: apply does not move items.
                items.Add(
                    new MediaItemChange(ContentChangeKind.Drifted, d.Id, d.Parent)
                    {
                        Changes = ["parent"],
                    }
                );
            else
                unchanged++;
        }

        items.AddRange(
            live.Where(l => !matched.Contains(l.Id))
                .Select(l => new MediaItemChange(ContentChangeKind.Removed, l.Id))
        );

        return new MediaDiff(items, unchanged)
        {
            LiveParents = liveById.ToDictionary(kv => kv.Key, kv => kv.Value.Parent),
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
