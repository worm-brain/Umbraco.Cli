using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands;

/// <summary>
/// JSON helpers shared by the body normalisers of the tree-shaped snapshot pipelines, content
/// (<c>ContentBodyNormaliser</c>) and media (<c>MediaBody</c>) (#274). Both read the same
/// Management API body shape (<c>variants</c> and <c>values</c> keyed by alias, culture and
/// segment), so the sort and string-read rules live here once.
/// </summary>
public static class SnapshotBody
{
    /// <summary>
    /// Rebuilds <paramref name="array"/> in key order. Ordinal comparison keeps the order the same
    /// on every machine; null sorts first (the invariant culture before any named one).
    /// </summary>
    /// <param name="array">The array to sort; its items are detached and re-added.</param>
    /// <param name="key">The sort key of an item.</param>
    /// <returns>A new array holding the same items in key order.</returns>
    public static JsonArray Sorted(
        JsonArray array,
        Func<JsonNode?, (string?, string?, string?)> key
    )
    {
        // A JsonNode can only have one parent, so the items are detached before the new array
        // takes them.
        var items = array.ToList();
        array.Clear();
        var ordered = items
            .OrderBy(i => key(i).Item1, StringComparer.Ordinal)
            .ThenBy(i => key(i).Item2, StringComparer.Ordinal)
            .ThenBy(i => key(i).Item3, StringComparer.Ordinal);
        return new JsonArray([.. ordered]);
    }

    /// <summary>
    /// Reduces the type reference in <paramref name="field"/> (<c>documentType</c>,
    /// <c>mediaType</c>) to its <c>id</c> (#346). The read also carries the type's <c>icon</c> and
    /// <c>collection</c>, which describe the schema rather than the item: comparing them would mark
    /// every item of a type changed when only the type's icon differs, and the item's write takes
    /// only the id anyway. A field that is absent or has no <c>id</c> is left as it is.
    /// </summary>
    /// <param name="body">The (cloned) body to change in place.</param>
    /// <param name="field">The type reference field.</param>
    public static void ReduceTypeToId(JsonObject body, string field)
    {
        if (body[field] is JsonObject type && type["id"] is { } id)
            body[field] = new JsonObject { ["id"] = id.DeepClone() };
    }

    /// <summary>A string property of an object, or null when absent or not a string.</summary>
    /// <param name="node">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null.</returns>
    public static string? Text(JsonNode? node, string name) =>
        node?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
