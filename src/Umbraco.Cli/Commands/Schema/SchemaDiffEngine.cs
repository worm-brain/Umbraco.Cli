using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The pure, client-free core of the pipeline (issue #68 / ADR 0004 §2): given a desired
/// snapshot and the live snapshot, it works out what would have to change to make the live
/// instance match the desired one. It performs no I/O, which is why the hardest logic in the
/// feature is also the cheapest to test.
///
/// Matching is <b>GUID-primary, alias-fallback</b>: an entity is paired by its <c>id</c> first;
/// failing that, by its human key (<c>alias</c> for document types and templates, <c>name</c>
/// for data types — data types have no alias). A key match whose ids differ is still a match
/// (the live entity is updated), flagged with <see cref="SchemaEntityChange.IdMismatch"/>. An
/// entity whose key matches more than one live entity is <see cref="SchemaChangeKind.Skipped"/>
/// rather than guessed (the data-type duplicate-name caveat).
/// </summary>
public static class SchemaDiffEngine
{
    private const string DocumentType = "documentType";
    private const string DataType = "dataType";
    private const string Template = "template";

    /// <summary>Compares a desired snapshot against the live one and returns the full diff.</summary>
    /// <param name="desired">The target schema (typically loaded from a snapshot file).</param>
    /// <param name="current">The live schema (typically a fresh export).</param>
    /// <returns>The diff across document types, data types, and templates.</returns>
    public static SchemaDiff Compare(SchemaSnapshot desired, SchemaSnapshot current) =>
        new(
            CompareKind(DocumentType, "alias", desired.DocumentTypes, current.DocumentTypes),
            CompareKind(DataType, "name", desired.DataTypes, current.DataTypes),
            CompareKind(Template, "alias", desired.Templates, current.Templates)
        );

    /// <summary>An entity reduced to what matching needs: its id, human key, and raw body.</summary>
    private readonly record struct Entry(Guid? Id, string Key, JsonNode Body);

    /// <summary>Diffs one entity kind by the GUID-primary, alias-fallback rules.</summary>
    /// <param name="kind">The entity-kind tag for the resulting changes.</param>
    /// <param name="keyField">The JSON field holding the human key (<c>alias</c> or <c>name</c>).</param>
    /// <param name="desiredBodies">The desired entities' raw bodies.</param>
    /// <param name="currentBodies">The live entities' raw bodies.</param>
    /// <returns>The diff for this kind.</returns>
    private static SchemaKindDiff CompareKind(
        string kind,
        string keyField,
        IReadOnlyList<JsonNode> desiredBodies,
        IReadOnlyList<JsonNode> currentBodies
    )
    {
        var desired = desiredBodies.Select(b => ToEntry(b, keyField)).ToList();
        var current = currentBodies.Select(b => ToEntry(b, keyField)).ToList();

        // Live lookups. byKey groups so we can detect an ambiguous key (>1 live match).
        var currentById = current.Where(e => e.Id is not null).ToDictionary(e => e.Id!.Value);
        var currentByKey = current
            .Where(e => !string.IsNullOrEmpty(e.Key))
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        var added = new List<SchemaEntityChange>();
        var changed = new List<SchemaEntityChange>();
        var skipped = new List<SchemaEntityChange>();
        var unchanged = 0;
        // Which live ids we paired to something desired; the rest are prune candidates.
        var matchedCurrentIds = new HashSet<Guid>();

        foreach (var d in desired)
        {
            // 1) GUID-primary: an exact id match is unambiguous.
            if (d.Id is { } did && currentById.TryGetValue(did, out var byId))
            {
                matchedCurrentIds.Add(did);
                Classify(kind, d, byId, idMismatch: false, changed, ref unchanged);
                continue;
            }

            // 2) alias-fallback: match on the human key.
            if (
                !string.IsNullOrEmpty(d.Key)
                && currentByKey.TryGetValue(d.Key, out var byKeyMatches)
            )
            {
                if (byKeyMatches.Count > 1)
                {
                    // Ambiguous: refuse to guess which live entity is meant.
                    skipped.Add(
                        new SchemaEntityChange(
                            kind,
                            SchemaChangeKind.Skipped,
                            d.Key,
                            d.Id,
                            null,
                            Note: $"'{d.Key}' matches {byKeyMatches.Count} live entities by "
                                + $"{keyField}; skipped to avoid an ambiguous update."
                        )
                    );
                    continue;
                }

                var match = byKeyMatches[0];
                if (match.Id is { } cid)
                    matchedCurrentIds.Add(cid);
                Classify(kind, d, match, idMismatch: d.Id != match.Id, changed, ref unchanged);
                continue;
            }

            // 3) No match on either key -> create (reuse the snapshot id for determinism).
            added.Add(
                new SchemaEntityChange(kind, SchemaChangeKind.Added, d.Key, d.Id, null)
                {
                    DesiredBody = d.Body,
                }
            );
        }

        // 4) Any live entity we never matched is a prune candidate.
        var removed = current
            .Where(e => e.Id is null || !matchedCurrentIds.Contains(e.Id.Value))
            .Select(e => new SchemaEntityChange(
                kind,
                SchemaChangeKind.Removed,
                e.Key,
                DesiredId: null,
                CurrentId: e.Id
            ))
            .ToList();

        return new SchemaKindDiff(added, changed, removed, skipped, unchanged);
    }

    /// <summary>
    /// Classifies a matched desired/live pair as Changed (bodies differ) or Unchanged. A change
    /// carries the desired body (for apply) and the live id (the update target).
    /// </summary>
    /// <param name="kind">The entity-kind tag.</param>
    /// <param name="desired">The desired-side entry.</param>
    /// <param name="current">The matched live-side entry.</param>
    /// <param name="idMismatch">Whether the pair was matched by key with differing ids.</param>
    /// <param name="changed">The bucket to append a Changed entry to.</param>
    /// <param name="unchanged">Running count of identical entities, incremented when equal.</param>
    private static void Classify(
        string kind,
        Entry desired,
        Entry current,
        bool idMismatch,
        List<SchemaEntityChange> changed,
        ref int unchanged
    )
    {
        if (JsonEqual(desired.Body, current.Body))
        {
            unchanged++;
            return;
        }

        changed.Add(
            new SchemaEntityChange(
                kind,
                SchemaChangeKind.Changed,
                desired.Key,
                desired.Id,
                current.Id,
                idMismatch
            )
            {
                DesiredBody = desired.Body,
            }
        );
    }

    /// <summary>Reduces a raw entity body to its id + human key for matching.</summary>
    /// <param name="body">The verbatim entity JSON.</param>
    /// <param name="keyField">The field carrying the human key.</param>
    /// <returns>The matching entry.</returns>
    private static Entry ToEntry(JsonNode body, string keyField)
    {
        Guid? id = null;
        if (
            body["id"] is JsonValue idValue
            && idValue.TryGetValue<string>(out var idStr)
            && Guid.TryParse(idStr, out var parsed)
        )
            id = parsed;

        var key = body[keyField]?.GetValue<string>() ?? "";
        return new Entry(id, key, body);
    }

    /// <summary>
    /// Order-insensitive deep JSON equality: objects compare by key set + values (property
    /// order ignored), arrays compare element-wise in order, values compare by their JSON text.
    /// Two <c>GET</c> bodies of the same unchanged entity compare equal regardless of the field
    /// order the server happened to emit.
    /// </summary>
    /// <param name="a">First node.</param>
    /// <param name="b">Second node.</param>
    /// <returns>True when the two nodes are structurally equal.</returns>
    private static bool JsonEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
            {
                if (oa.Count != ob.Count)
                    return false;
                foreach (var (name, value) in oa)
                {
                    if (!ob.TryGetPropertyValue(name, out var other) || !JsonEqual(value, other))
                        return false;
                }
                return true;
            }
            case JsonArray aa when b is JsonArray ab:
            {
                if (aa.Count != ab.Count)
                    return false;
                for (var i = 0; i < aa.Count; i++)
                {
                    if (!JsonEqual(aa[i], ab[i]))
                        return false;
                }
                return true;
            }
            case JsonObject or JsonArray:
            case var _ when b is JsonObject or JsonArray:
                // Mismatched container vs value.
                return false;
            default:
                // Both are values (string/number/bool): compare canonical JSON text.
                return a.ToJsonString() == b.ToJsonString();
        }
    }
}
