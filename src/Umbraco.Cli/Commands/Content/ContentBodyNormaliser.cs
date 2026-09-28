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
/// <c>state</c>; and the document type reference is cut to its <c>id</c> (#346), since its
/// <c>icon</c> and <c>collection</c> belong to the schema. Publish state is not part of the body an update writes, so it is compared on
/// its own rather than as a body difference. <c>values</c> and <c>variants</c> are sorted by alias/culture/segment, because the
/// server does not promise an order and the comparison is order-sensitive for arrays.
///
/// The diff compares <see cref="ForComparison"/>, which also leaves out values of read-only
/// editors (#291): Umbraco ignores a value sent for them, so the target keeps its own and the
/// document would never diff clean. Apply still sends them as before; it cannot change them.
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

    /// <summary>
    /// The property editors whose values Umbraco does not take from a create or update (#291).
    /// <c>Umbraco.Label</c> is the core editor for values set by code; its value editor is read
    /// only, so the Management API saves nothing for it. Other editors are compared as usual: a
    /// value that differs there is one apply can write.
    /// </summary>
    public static readonly IReadOnlySet<string> ReadOnlyEditors = new HashSet<string>(
        ["Umbraco.Label"],
        StringComparer.OrdinalIgnoreCase
    );

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
        SnapshotBody.ReduceTypeToId(doc, "documentType");

        if (doc["variants"] is JsonArray variants)
        {
            foreach (var variant in variants.OfType<JsonObject>())
            foreach (var field in VariantNoise)
                variant.Remove(field);
            doc["variants"] = SnapshotBody.Sorted(
                variants,
                v => (null, SnapshotBody.Text(v, "culture"), SnapshotBody.Text(v, "segment"))
            );
        }

        if (doc["values"] is JsonArray values)
            doc["values"] = SnapshotBody.Sorted(
                values,
                v =>
                    (
                        SnapshotBody.Text(v, "alias"),
                        SnapshotBody.Text(v, "culture"),
                        SnapshotBody.Text(v, "segment")
                    )
            );

        return doc;
    }

    /// <summary>
    /// The body the diff compares: <see cref="Normalise"/>, without the values of read-only
    /// editors (<see cref="ReadOnlyEditors"/>, #291), which a promotion can never change.
    /// </summary>
    /// <param name="body">A verbatim or already normalised document body; not mutated.</param>
    /// <returns>The normalised clone without read-only values.</returns>
    public static JsonNode ForComparison(JsonNode body)
    {
        var doc = Normalise(body);
        if (doc["values"] is JsonArray values)
            doc["values"] = new JsonArray([
                .. values.Where(v => !IsReadOnly(v)).Select(v => v?.DeepClone()),
            ]);
        return doc;
    }

    /// <summary>The values of read-only editors in <paramref name="body"/> (#291).</summary>
    /// <param name="body">A document body.</param>
    /// <returns>Each read-only value object, in body order.</returns>
    public static IEnumerable<JsonObject> ReadOnlyValues(JsonNode? body) =>
        (body?["values"] as JsonArray ?? []).OfType<JsonObject>().Where(IsReadOnly);

    /// <summary>Whether a value belongs to a read-only editor, by its <c>editorAlias</c>.</summary>
    /// <param name="value">A value object from a document body.</param>
    /// <returns>True for a read-only editor's value.</returns>
    private static bool IsReadOnly(JsonNode? value) =>
        SnapshotBody.Text(value, "editorAlias") is { } editor && ReadOnlyEditors.Contains(editor);
}
