using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The within-kind orderings the schema kind table (<see cref="SchemaKinds.All"/>) assigns to each
/// kind's creates and deletes (#273). The cross-kind order is the table's; these only order the
/// entities of one kind among themselves, so a referenced entity is created before its referrer and
/// deleted after it.
/// </summary>
internal static class SchemaOrder
{
    /// <summary>
    /// Creates ordered so an entity referenced by id somewhere in another's body (a composition, a
    /// master template, a dictionary item's parent) is created first. Ids are found anywhere in the
    /// body, so no JSON path is hard-coded.
    /// </summary>
    /// <param name="added">One kind's added entities.</param>
    /// <returns>The ordered creates.</returns>
    public static IEnumerable<SchemaEntityChange> CreatesByIdReference(
        IReadOnlyList<SchemaEntityChange> added
    ) =>
        TopoOrder(
            added,
            c => c.DesiredId?.ToString(),
            c => c.DesiredBody is null ? [] : ExtractGuids(c.DesiredBody).Select(g => g.ToString())
        );

    /// <summary>Language creates ordered so a language's fallback (by ISO code) is created first.</summary>
    /// <param name="added">The added languages.</param>
    /// <returns>The ordered creates.</returns>
    public static IEnumerable<SchemaEntityChange> LanguageCreates(
        IReadOnlyList<SchemaEntityChange> added
    ) => TopoOrder(added, c => c.Identity, c => Fallback(c.DesiredBody));

    /// <summary>Deletes in enumeration order: Umbraco rejects a delete that is still depended on, which fail-fast surfaces and a re-run resolves.</summary>
    /// <param name="removed">One kind's removed entities.</param>
    /// <returns>The deletes, unchanged.</returns>
    public static IEnumerable<SchemaEntityChange> AsEnumerated(
        IReadOnlyList<SchemaEntityChange> removed
    ) => removed;

    /// <summary>
    /// Dictionary deletes children first, from the live bodies' parents: deleting a parent first
    /// would take the child with it and fail the child's own delete.
    /// </summary>
    /// <param name="removed">The removed dictionary items.</param>
    /// <returns>The ordered deletes.</returns>
    public static IEnumerable<SchemaEntityChange> DictionaryDeletes(
        IReadOnlyList<SchemaEntityChange> removed
    ) =>
        Enumerable.Reverse(
            TopoOrder(
                removed,
                c => c.CurrentId?.ToString(),
                c => SchemaBodies.ParentOf(c.CurrentBody) is { } parent ? [parent.ToString()] : []
            )
        );

    /// <summary>Language deletes ordered so a language goes before the language it falls back to.</summary>
    /// <param name="removed">The removed languages.</param>
    /// <returns>The ordered deletes.</returns>
    public static IEnumerable<SchemaEntityChange> LanguageDeletes(
        IReadOnlyList<SchemaEntityChange> removed
    ) => Enumerable.Reverse(TopoOrder(removed, c => c.Identity, c => Fallback(c.CurrentBody)));

    /// <summary>
    /// Static-file creates (#292): folders before files, shallowest first, then by path, so a
    /// file's folder always exists before the file.
    /// </summary>
    /// <param name="added">One static-file kind's added entries.</param>
    /// <returns>The ordered creates.</returns>
    public static IEnumerable<SchemaEntityChange> FileCreates(
        IReadOnlyList<SchemaEntityChange> added
    ) =>
        added
            .OrderBy(c => SchemaStaticFiles.IsFolder(c.DesiredBody) ? 0 : 1)
            .ThenBy(c => SchemaStaticFiles.Depth(c.Identity))
            .ThenBy(c => c.Identity, StringComparer.Ordinal);

    /// <summary>
    /// Static-file deletes (#292): files before folders, deepest folder first, because Umbraco
    /// refuses to delete a folder that is not empty. A pruned folder never holds a kept entry: the
    /// diff implies a folder entry for every folder a kept entry sits in.
    /// </summary>
    /// <param name="removed">One static-file kind's removed entries.</param>
    /// <returns>The ordered deletes.</returns>
    public static IEnumerable<SchemaEntityChange> FileDeletes(
        IReadOnlyList<SchemaEntityChange> removed
    ) =>
        removed
            .OrderBy(c => SchemaStaticFiles.IsFolder(c.CurrentBody) ? 1 : 0)
            .ThenByDescending(c => SchemaStaticFiles.Depth(c.Identity))
            .ThenBy(c => c.Identity, StringComparer.Ordinal);

    /// <summary>A language body's fallback ISO code, as a reference list.</summary>
    /// <param name="body">The language body.</param>
    /// <returns>The fallback code, or nothing.</returns>
    private static IEnumerable<string> Fallback(JsonNode? body) =>
        body?["fallbackIsoCode"] is JsonValue v
        && v.TryGetValue<string>(out var iso)
        && !string.IsNullOrEmpty(iso)
            ? [iso]
            : [];

    /// <summary>
    /// Orders entities so that any entity referencing another entity in the batch comes *after*
    /// the entity it references. What an entity is called and what it references are supplied, so
    /// the one sort serves ids found anywhere in a body and ISO codes alike. A reference cycle
    /// (which Umbraco itself disallows) is broken by falling back to input order for the entangled
    /// remainder rather than looping.
    /// </summary>
    /// <param name="changes">The changes to order.</param>
    /// <param name="selfKey">An entity's own key, or null when it has none.</param>
    /// <param name="references">The keys an entity references.</param>
    /// <returns>The dependency-ordered changes.</returns>
    private static List<SchemaEntityChange> TopoOrder(
        IReadOnlyList<SchemaEntityChange> changes,
        Func<SchemaEntityChange, string?> selfKey,
        Func<SchemaEntityChange, IEnumerable<string>> references
    )
    {
        if (changes.Count <= 1)
            return changes.ToList();

        // Work by index throughout: SchemaEntityChange is a value-equal record, so keying maps by
        // the change itself would throw on two equal entries — indices are always distinct.
        var inSet = changes.Select(selfKey).OfType<string>().ToHashSet();

        // deps[i] = the in-batch keys that changes[i] references (excluding its own key).
        var deps = new List<HashSet<string>>(changes.Count);
        foreach (var c in changes)
        {
            var self = selfKey(c);
            deps.Add(references(c).Where(k => inSet.Contains(k) && k != self).ToHashSet());
        }

        var ordered = new List<SchemaEntityChange>();
        var emitted = new HashSet<string>();
        var remaining = Enumerable.Range(0, changes.Count).ToList();

        while (remaining.Count > 0)
        {
            // Emit every entity whose in-batch dependencies are all already emitted.
            var ready = remaining.Where(i => deps[i].All(emitted.Contains)).ToList();

            if (ready.Count == 0)
            {
                // Cycle (or a self-referential remainder): emit the rest in input order so we make
                // progress instead of spinning. Umbraco will reject a genuine impossible order.
                ordered.AddRange(remaining.Select(i => changes[i]));
                break;
            }

            foreach (var i in ready)
            {
                ordered.Add(changes[i]);
                if (selfKey(changes[i]) is { } key)
                    emitted.Add(key);
                remaining.Remove(i);
            }
        }

        return ordered;
    }

    /// <summary>Recursively collects every GUID-valued string in a JSON body.</summary>
    /// <param name="node">The node to scan.</param>
    /// <returns>Every parseable GUID found, with duplicates.</returns>
    private static IEnumerable<Guid> ExtractGuids(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    if (value is null)
                        continue;
                    foreach (var g in ExtractGuids(value))
                        yield return g;
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is null)
                        continue;
                    foreach (var g in ExtractGuids(item))
                        yield return g;
                }
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
                    yield return guid;
                break;
        }
    }
}
