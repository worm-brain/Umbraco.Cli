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
/// culture). Anything else is matched by index (<c>containers[2].name</c>), and an index-matched
/// array whose length changed is reported as a whole. A property value (an item with both
/// <c>alias</c> and <c>value</c>) is reported as a whole too: its <c>value</c> can be a block
/// editor's entire JSON, which is not a useful place to point at. A keyed array whose items all
/// match but in another order is reported at the array's own path, so the result is empty exactly
/// when <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/> says the bodies are equal.
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

    private static void Walk(string path, JsonNode? a, JsonNode? b, List<string> paths)
    {
        if (JsonNode.DeepEquals(a, b))
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

        var before = paths.Count;
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

        // Every item matched, yet the arrays differ: only the order did. Order is part of the
        // comparison (a doc type's property order is meaningful), so it is reported, at the array.
        if (paths.Count == before)
            paths.Add(path.Length == 0 ? "$" : path);
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
        return null;
    }

    private static bool IsPropertyValue(JsonNode? item) =>
        item is JsonObject o && o.ContainsKey("alias") && o.ContainsKey("value");

    private static string? Text(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
