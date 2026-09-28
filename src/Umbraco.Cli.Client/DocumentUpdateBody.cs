using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// Builds the body of a document update by overlaying a request onto the document as the
/// Management API returned it.
/// <para>
/// This exists because <c>PUT /document/{id}</c> is replace-semantics: whatever the body omits is
/// deleted from the document. Rather than construct a typed body - which can only carry the
/// fields the CLI models, and so silently dropped the template (#178) and every unlisted value
/// (#179) - the caller reads the document verbatim and hands it here to be patched.
/// </para>
/// <para>
/// Pure and synchronous by design: the merge rules are the part worth testing, and keeping them
/// free of the HTTP client means they can be tested directly rather than through a handler.
/// </para>
/// </summary>
public static class DocumentUpdateBody
{
    /// <summary>The fields a value entry may carry on the way back to the server.</summary>
    private static readonly string[] ValueFields = ["alias", "culture", "segment", "value"];

    /// <summary>The fields a variant entry may carry on the way back to the server.</summary>
    private static readonly string[] VariantFields = ["culture", "segment", "name"];

    /// <summary>
    /// Returns <paramref name="document"/> with the request's values and variants overlaid.
    /// <para>
    /// Values are matched on (alias, culture, segment) and variants on (culture, segment); a
    /// request entry replaces the matching one or is appended when there is none. Every entry in
    /// the result - carried over or newly supplied - is projected to the request's own fields, so
    /// the body has one shape throughout and read-only extras such as <c>editorAlias</c> and
    /// <c>state</c> are not echoed back.
    /// </para>
    /// <para>
    /// The document's other fields, the template above all, are left exactly as they were read.
    /// </para>
    /// </summary>
    /// <param name="document">The document as read, mutated in place and returned.</param>
    /// <param name="values">Values to overlay.</param>
    /// <param name="variants">Variants to overlay.</param>
    /// <param name="mode">
    /// With <see cref="WriteMode.Replace"/> the request's values and variants stand alone rather
    /// than being merged into the document's. The template is unaffected either way.
    /// </param>
    /// <returns>The same <paramref name="document"/> instance, patched.</returns>
    /// <exception cref="ApiException">
    /// A variant with no culture was supplied for a document that varies by culture, which does
    /// not identify a variant to change. Carries status 400 so the command layer reports it as a
    /// rejected request rather than crashing.
    /// </exception>
    public static JsonObject Merge(
        JsonObject document,
        IEnumerable<ContentValue> values,
        IEnumerable<ContentVariant> variants,
        WriteMode mode = WriteMode.Merge
    )
    {
        var replace = mode == WriteMode.Replace;
        var currentValues = replace ? null : document["values"] as JsonArray;
        var currentVariants = replace ? null : document["variants"] as JsonArray;

        GuardVariantCultures(document["variants"] as JsonArray, variants);

        document["values"] = Upsert(
            currentValues,
            values.Select(v => new JsonObject
            {
                ["alias"] = v.Alias,
                ["culture"] = v.Culture,
                ["segment"] = v.Segment,
                ["value"] = JsonSerializer.SerializeToNode(v.Value),
            }),
            ValueFields,
            ["alias", "culture", "segment"]
        );

        document["variants"] = Upsert(
            currentVariants,
            variants.Select(v => new JsonObject
            {
                ["culture"] = v.Culture,
                ["segment"] = v.Segment,
                ["name"] = v.Name,
            }),
            VariantFields,
            ["culture", "segment"]
        );

        return document;
    }

    /// <summary>
    /// Rejects a variant that names no culture when the document has culture-bearing variants.
    /// Such an entry matches nothing, so it would be appended - inventing a null-culture variant
    /// alongside the real ones and leaving the caller's intended rename undone. Umbraco rejects
    /// that mix anyway ("Cannot publish invariant culture when the document varies by culture"),
    /// so failing here with a usable message beats sending it.
    /// </summary>
    /// <param name="current">The document's current variants.</param>
    /// <param name="requested">The variants from the request.</param>
    /// <exception cref="ApiException">The request is ambiguous, as described above.</exception>
    private static void GuardVariantCultures(
        JsonArray? current,
        IEnumerable<ContentVariant> requested
    )
    {
        var cultures = (current ?? [])
            .Select(v => Field(v, "culture"))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();
        if (cultures.Count == 0)
            return;

        if (requested.Any(v => string.IsNullOrEmpty(v.Culture)))
            throw new ApiException(
                "This document varies by culture, so each variant must name one. "
                    + $"Set \"culture\" on the variant (the document has: {string.Join(", ", cultures)})."
            )
            {
                ResponseStatusCode = 400,
            };
    }

    /// <summary>
    /// Lays <paramref name="over"/> on <paramref name="under"/> with the rule <see cref="Merge"/>
    /// uses for values: an entry with the same alias + culture + segment is replaced in place,
    /// anything else is appended. For combining a body with command-line flags (#220) before the
    /// merge, so both steps key entries the same way.
    /// </summary>
    /// <param name="under">The body's values.</param>
    /// <param name="over">The values that win (e.g. from flags).</param>
    /// <returns>The combined values.</returns>
    public static List<ContentValue> Overlay(
        IEnumerable<ContentValue> under,
        IEnumerable<ContentValue> over
    ) => OverlayBy(under, over, v => (v.Alias, v.Culture, v.Segment));

    /// <summary>The variant twin of <see cref="Overlay(IEnumerable{ContentValue}, IEnumerable{ContentValue})"/>, keyed on culture + segment.</summary>
    /// <param name="under">The body's variants.</param>
    /// <param name="over">The variants that win.</param>
    /// <returns>The combined variants.</returns>
    public static List<ContentVariant> Overlay(
        IEnumerable<ContentVariant> under,
        IEnumerable<ContentVariant> over
    ) => OverlayBy(under, over, v => (v.Culture, v.Segment));

    /// <summary>Replace-in-place-else-append, on a key.</summary>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="under">The base entries.</param>
    /// <param name="over">The winning entries.</param>
    /// <param name="key">The key of an entry.</param>
    /// <returns>The combined entries.</returns>
    private static List<T> OverlayBy<T, TKey>(
        IEnumerable<T> under,
        IEnumerable<T> over,
        Func<T, TKey> key
    )
    {
        var result = under.ToList();
        foreach (var entry in over)
        {
            var at = result.FindIndex(u =>
                EqualityComparer<TKey>.Default.Equals(key(u), key(entry))
            );
            if (at >= 0)
                result[at] = entry;
            else
                result.Add(entry);
        }
        return result;
    }

    /// <summary>
    /// Merges <paramref name="requested"/> into <paramref name="current"/>, keyed on
    /// <paramref name="keyFields"/>: a request entry replaces the entry with the same key or is
    /// appended. Every entry in the result is projected to <paramref name="keptFields"/>.
    /// </summary>
    /// <param name="current">The current entries; null is treated as empty.</param>
    /// <param name="requested">The entries to overlay.</param>
    /// <param name="keptFields">The fields an entry may carry in the result.</param>
    /// <param name="keyFields">The fields that together identify an entry.</param>
    /// <returns>The merged array.</returns>
    private static JsonArray Upsert(
        JsonArray? current,
        IEnumerable<JsonObject> requested,
        string[] keptFields,
        string[] keyFields
    )
    {
        var merged = (current ?? [])
            .Select(n => Project(n as JsonObject, keptFields))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        foreach (var entry in requested)
        {
            var projected = Project(entry, keptFields)!;
            var existing = merged.FindIndex(n => SameKey(n, projected, keyFields));
            if (existing >= 0)
                merged[existing] = projected;
            else
                merged.Add(projected);
        }

        return [.. merged];
    }

    /// <summary>Copies only the named fields out of an entry, dropping everything else.</summary>
    /// <param name="node">The entry to project; null yields null.</param>
    /// <param name="fields">The fields to keep.</param>
    /// <returns>A new object carrying just those fields.</returns>
    private static JsonObject? Project(JsonObject? node, string[] fields)
    {
        if (node is null)
            return null;

        var projected = new JsonObject();
        foreach (var field in fields)
            projected[field] = node[field]?.DeepClone();
        return projected;
    }

    /// <summary>Whether two entries agree on every key field.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <param name="keyFields">The fields that identify an entry.</param>
    /// <returns>True when all key fields match.</returns>
    private static bool SameKey(JsonObject left, JsonObject right, string[] keyFields) =>
        keyFields.All(f => Field(left, f) == Field(right, f));

    /// <summary>Reads a string field, treating absent and JSON null alike.</summary>
    /// <param name="node">The node to read from.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null when absent or null.</returns>
    private static string? Field(JsonNode? node, string name) =>
        node?[name] is { } v && v.GetValueKind() is not JsonValueKind.Null
            ? v.GetValue<string>()
            : null;
}
