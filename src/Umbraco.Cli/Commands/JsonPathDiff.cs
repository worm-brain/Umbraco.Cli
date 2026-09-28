using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Names where two JSON bodies differ, for the <c>changes</c> list of a diff row (#229), so a
/// "Changed" row says what changed without a manual export and <c>jq</c> diff.
///
/// Paths are dotted object keys (<c>template.id</c>). An array of Umbraco-shaped items is matched
/// by the item's identity rather than its position, so an inserted item does not report every
/// later one: an item with an <c>alias</c> is named by it, plus its culture and segment when set
/// (<c>values.title[en-US]</c>, <c>properties.title</c>); a variant, which has a <c>culture</c> but
/// no alias, is named by its culture (<c>variants[en-US].name</c>, <c>[invariant]</c> for the null
/// culture); any other item with an <c>id</c>, such as a document type's container, is named by
/// it (<c>containers[3f2a...].name</c>), and an item that holds exactly one reference, such as an
/// <c>allowedDocumentTypes</c> or <c>compositions</c> entry, by the id it references
/// (<c>allowedDocumentTypes[3f2a...].sortOrder</c>). GUIDs match and compare ignoring letter case.
/// Anything else is matched by index (<c>tags[2]</c>), and an index-matched array whose length
/// changed is reported as a whole. A property value (an item with both <c>alias</c> and <c>value</c>) is
/// reported as a whole too: its <c>value</c> can be a block editor's entire JSON, which is not a
/// useful place to point at.
/// <para>
/// A keyed array is compared as a set: the same items in another order are not a difference
/// (#350). Umbraco carries order in the items themselves (a property's or container's
/// <c>sortOrder</c> and <c>parent</c>), not in array position, and does not return items in the
/// order they were written, so a hand-written snapshot in its own order must still compare equal;
/// a real reorder shows as a <c>sortOrder</c> change. Content and media bodies sort their keyed
/// arrays before comparing anyway.
/// </para>
/// A member absent on one side and null on the other is not a difference, at any depth, so the
/// result is empty exactly when the bodies are equal once such members are ignored and keyed
/// arrays are read as sets.
/// </summary>
public static class JsonPathDiff
{
    /// <summary>Lists the paths at which <paramref name="desired"/> and <paramref name="current"/> differ.</summary>
    /// <param name="desired">The snapshot-side body.</param>
    /// <param name="current">The live-side body.</param>
    /// <returns>The differing paths in document order (desired's keys first); empty when equal. <c>$</c> names the root.</returns>
    public static IReadOnlyList<string> Paths(JsonNode? desired, JsonNode? current)
    {
        var paths = new List<string>();
        Walk("", desired, current, paths);
        return paths;
    }

    /// <summary>
    /// Compares two nodes at <paramref name="path"/> and appends each path where they differ.
    /// Two GUID strings that differ only in letter case are equal.
    /// </summary>
    /// <param name="path">The nodes' path; empty at the root.</param>
    /// <param name="a">The desired-side node.</param>
    /// <param name="b">The live-side node.</param>
    /// <param name="paths">The differing paths found so far; appended to.</param>
    private static void Walk(string path, JsonNode? a, JsonNode? b, List<string> paths)
    {
        if (JsonNode.DeepEquals(a, b) || SameGuid(a, b))
            return;

        switch (a, b)
        {
            case (JsonObject oa, JsonObject ob):
                foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                    Walk(path.Length == 0 ? key : $"{path}.{key}", oa[key], ob[key], paths);
                return;

            case (JsonArray aa, JsonArray ab):
                WalkArray(path, aa, ab, paths);
                return;

            default:
                paths.Add(path.Length == 0 ? "$" : path);
                return;
        }
    }

    /// <summary>
    /// Compares two arrays: by item identity when every item on both sides has a unique one
    /// (order ignored, #350), else by index.
    /// </summary>
    /// <param name="path">The arrays' path.</param>
    /// <param name="a">The desired-side array.</param>
    /// <param name="b">The live-side array.</param>
    /// <param name="paths">The differing paths found so far; appended to.</param>
    private static void WalkArray(string path, JsonArray a, JsonArray b, List<string> paths)
    {
        var keyedA = Keyed(a);
        var keyedB = Keyed(b);
        if (keyedA is null || keyedB is null)
        {
            if (a.Count != b.Count)
            {
                paths.Add(path.Length == 0 ? "$" : path);
                return;
            }
            for (var i = 0; i < a.Count; i++)
                Walk($"{path}[{i}]", a[i], b[i], paths);
            return;
        }

        foreach (var label in keyedA.Keys.Union(keyedB.Keys))
        {
            var itemA = keyedA.GetValueOrDefault(label);
            var itemB = keyedB.GetValueOrDefault(label);
            // A leading "." label (an alias) joins like a key; a "[..]" label (a culture) appends.
            var itemPath =
                label[0] == '.' ? (path.Length == 0 ? label[1..] : path + label) : path + label;

            if (IsPropertyValue(itemA) || IsPropertyValue(itemB))
            {
                if (!JsonNode.DeepEquals(itemA, itemB))
                    paths.Add(itemPath);
                continue;
            }
            Walk(itemPath, itemA, itemB, paths);
        }
        // Items matched by identity in a different order add nothing: order is not compared for a
        // keyed array (#350).
    }

    /// <summary>
    /// Indexes an array by item identity, or returns null when any item has no identity or two
    /// share one (then only position can match them). Insertion order is kept.
    /// </summary>
    private static Dictionary<string, JsonNode?>? Keyed(JsonArray array)
    {
        var byLabel = new Dictionary<string, JsonNode?>();
        foreach (var item in array)
        {
            if (Label(item) is not { } label || !byLabel.TryAdd(label, item))
                return null;
        }
        return byLabel;
    }

    /// <summary>
    /// An item's identity label: <c>.alias</c> (plus a culture/segment qualifier), else
    /// <c>[culture]</c> for a variant, else <c>[id]</c> for any other item with an id, else
    /// <c>[id]</c> of the one reference it holds (<c>documentType.id</c>) (#350). GUIDs are
    /// lower-cased.
    /// </summary>
    /// <param name="item">An array item.</param>
    /// <returns>The label, or null when the item has no identity.</returns>
    private static string? Label(JsonNode? item)
    {
        if (item is not JsonObject o)
            return null;

        var culture = Text(o, "culture");
        var segment = Text(o, "segment");
        var qualifier = (culture, segment) switch
        {
            (null, null) => "",
            (_, null) => $"[{culture}]",
            _ => $"[{culture ?? "invariant"}/{segment}]",
        };

        if (Text(o, "alias") is { } alias)
            return $".{alias}{qualifier}";
        if (o.ContainsKey("culture"))
            return qualifier.Length > 0 ? qualifier : "[invariant]";
        // #350: an item with neither, but an id (a type's container), is matched by the id, so a
        // container list in another order than the live one still pairs each container.
        if (Text(o, "id") is { } id)
            return $"[{Canonical(id)}]";
        // Else an item that points at one thing by id and says nothing else about identity (an
        // allowedDocumentTypes or compositions entry, {"documentType":{"id":..},...}) is matched
        // by that id.
        var references = o.Where(p => p.Value is JsonObject r && Text(r, "id") is not null)
            .Select(p => Text(p.Value!.AsObject(), "id")!)
            .ToList();
        return references.Count == 1 ? $"[{Canonical(references[0])}]" : null;
    }

    /// <summary>
    /// A GUID in its lower-case form, so an id written in upper case by hand matches the live one;
    /// any other text is returned as it is.
    /// </summary>
    /// <param name="id">The id text.</param>
    /// <returns>The canonical id.</returns>
    private static string Canonical(string id) =>
        Guid.TryParse(id, out var guid) ? guid.ToString() : id;

    /// <summary>Whether two nodes are strings naming the same GUID, whatever their letter case.</summary>
    /// <param name="a">A node.</param>
    /// <param name="b">Another node.</param>
    /// <returns>True when both are GUID strings with the same value.</returns>
    private static bool SameGuid(JsonNode? a, JsonNode? b) =>
        a is JsonValue va
        && b is JsonValue vb
        && va.TryGetValue<string>(out var sa)
        && vb.TryGetValue<string>(out var sb)
        && Guid.TryParse(sa, out var ga)
        && Guid.TryParse(sb, out var gb)
        && ga == gb;

    private static bool IsPropertyValue(JsonNode? item) =>
        item is JsonObject o && o.ContainsKey("alias") && o.ContainsKey("value");

    private static string? Text(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
