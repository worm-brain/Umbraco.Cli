using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Reduces a verbatim <c>GET /document/{id}</c> body to the part a promotion carries between
/// instances (#224): what an editor wrote, not what the instance recorded about it. The diff runs
/// on both sides of the comparison through this, and apply sends the result, so two instances
/// holding the same content compare equal and a second apply is a no-op.
///
/// Stripped, because they always differ on another instance or are server-owned: the top-level
/// <c>isTrashed</c> and <c>flags</c>, and each variant's <c>createDate</c>, <c>updateDate</c>,
/// <c>publishDate</c>, <c>scheduledPublishDate</c>, <c>scheduledUnpublishDate</c>, <c>flags</c> and
/// <c>state</c>. Publish state is not part of the body an update writes, so it is compared on
/// its own rather than as a body difference. <c>values</c> and <c>variants</c> are sorted by alias/culture/segment, because the
/// server does not promise an order and the comparison is order-sensitive for arrays.
/// </summary>
public static class ContentBodyNormaliser
{
    private static readonly string[] TopLevelNoise = ["isTrashed", "flags"];

    private static readonly string[] VariantNoise =
    [
        "createDate",
        "updateDate",
        "publishDate",
        "scheduledPublishDate",
        "scheduledUnpublishDate",
        "flags",
        "state",
    ];

    /// <summary>Returns a normalised copy of <paramref name="body"/>; the input is not mutated.</summary>
    /// <param name="body">A verbatim document body (a snapshot's or a live export's).</param>
    /// <returns>The normalised clone. A body that is not a JSON object is returned cloned as is.</returns>
    public static JsonNode Normalise(JsonNode body)
    {
        var clone = body.DeepClone();
        if (clone is not JsonObject doc)
            return clone;

        foreach (var field in TopLevelNoise)
            doc.Remove(field);

        if (doc["variants"] is JsonArray variants)
        {
            foreach (var variant in variants.OfType<JsonObject>())
            foreach (var field in VariantNoise)
                variant.Remove(field);
            doc["variants"] = Sorted(variants, v => (null, Text(v, "culture"), Text(v, "segment")));
        }

        if (doc["values"] is JsonArray values)
            doc["values"] = Sorted(
                values,
                v => (Text(v, "alias"), Text(v, "culture"), Text(v, "segment"))
            );

        return doc;
    }

    /// <summary>
    /// Rebuilds <paramref name="array"/> in key order. Ordinal comparison keeps the order the same
    /// on every machine; null sorts first (the invariant culture before any named one).
    /// </summary>
    /// <param name="array">The array to sort; its items are detached and re-added.</param>
    /// <param name="key">The sort key of an item.</param>
    /// <returns>A new array holding the same items in key order.</returns>
    internal static JsonArray Sorted(
        JsonArray array,
        Func<JsonNode?, (string?, string?, string?)> key
    )
    {
        var items = array.ToList();
        array.Clear();
        var ordered = items
            .OrderBy(i => key(i).Item1, StringComparer.Ordinal)
            .ThenBy(i => key(i).Item2, StringComparer.Ordinal)
            .ThenBy(i => key(i).Item3, StringComparer.Ordinal);
        return new JsonArray([.. ordered]);
    }

    /// <summary>A string property of an object, or null when absent or not a string.</summary>
    /// <param name="node">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null.</returns>
    internal static string? Text(JsonNode? node, string name) =>
        node?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
