using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The pure, client-free core of the pipeline (issue #68 / ADR 0005 §2): given a desired
/// snapshot and the live snapshot, it works out what would have to change to make the live
/// instance match the desired one. It performs no I/O, which is why the hardest logic in the
/// feature is also the cheapest to test.
///
/// Matching is <b>GUID-primary, alias-fallback</b>, done in two passes so the two keys never
/// fight over the same live entity:
/// <list type="number">
/// <item>Pass 1 pairs desired and live entities by <c>id</c>.</item>
/// <item>Pass 2 matches the still-unpaired desired entities by their human key (<c>alias</c> for
/// document types and templates, <c>name</c> for data types) against the still-unpaired live
/// entities only. A key match whose ids differ is still a match (the live entity is updated),
/// flagged with <see cref="SchemaEntityChange.IdMismatch"/>.</item>
/// </list>
/// A desired entity whose key matches more than one remaining live entity is
/// <see cref="SchemaChangeKind.Skipped"/> rather than guessed — and the ambiguous live
/// candidates are marked matched so a later <c>--prune</c> does not delete them (the whole point
/// of refusing to guess).
/// </summary>
public static class SchemaDiffEngine
{
    /// <summary>Compares a desired snapshot against the live one and returns the full diff.</summary>
    /// <param name="desired">The target schema (typically loaded from a snapshot file).</param>
    /// <param name="current">The live schema (typically a fresh export).</param>
    /// <returns>The diff across document types, data types, and templates.</returns>
    public static SchemaDiff Compare(SchemaSnapshot desired, SchemaSnapshot current) =>
        new(
            CompareKind(SchemaKinds.DocumentType, "alias", desired.DocumentTypes, current.DocumentTypes),
            CompareKind(SchemaKinds.DataType, "name", desired.DataTypes, current.DataTypes),
            CompareKind(SchemaKinds.Template, "alias", desired.Templates, current.Templates)
        );

    /// <summary>An entity reduced to what matching needs: its id, human key, and raw body.</summary>
    private readonly record struct Entry(Guid? Id, string Key, JsonNode Body);

    /// <summary>Diffs one entity kind by the GUID-primary, alias-fallback rules (two passes).</summary>
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

        var added = new List<SchemaEntityChange>();
        var changed = new List<SchemaEntityChange>();
        var skipped = new List<SchemaEntityChange>();
        var unchanged = 0;

        // Live ids we have paired to a desired entity (or deliberately protected from prune).
        var matchedLiveIds = new HashSet<Guid>();
        var currentById = current.Where(e => e.Id is not null).ToDictionary(e => e.Id!.Value);

        // ── Pass 1: GUID-primary. An exact id match is unambiguous. ──────────────
        var unmatchedDesired = new List<Entry>();
        foreach (var d in desired)
        {
            if (d.Id is { } did && currentById.TryGetValue(did, out var live))
            {
                matchedLiveIds.Add(did);
                Classify(kind, d, live, idMismatch: false, changed, ref unchanged);
            }
            else
            {
                unmatchedDesired.Add(d);
            }
        }

        // ── Pass 2: alias/name fallback over the live entities NOT already id-matched. ──
        var unmatchedByKey = current
            .Where(e =>
                !string.IsNullOrEmpty(e.Key)
                && (e.Id is null || !matchedLiveIds.Contains(e.Id.Value))
            )
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var d in unmatchedDesired)
        {
            if (
                string.IsNullOrEmpty(d.Key)
                || !unmatchedByKey.TryGetValue(d.Key, out var matches)
                || matches.Count == 0
            )
            {
                // No match on either key -> create (reuse the snapshot id for determinism).
                added.Add(
                    new SchemaEntityChange(kind, SchemaChangeKind.Added, d.Key, d.Id, null)
                    {
                        DesiredBody = d.Body,
                    }
                );
                continue;
            }

            if (matches.Count > 1)
            {
                // Ambiguous: refuse to guess which live entity is meant, AND protect every
                // candidate from prune (otherwise the "safe" skip would still delete them).
                foreach (var m in matches)
                    if (m.Id is { } mid)
                        matchedLiveIds.Add(mid);
                skipped.Add(
                    new SchemaEntityChange(
                        kind,
                        SchemaChangeKind.Skipped,
                        d.Key,
                        d.Id,
                        null,
                        Note: $"'{d.Key}' matches {matches.Count} live entities by {keyField}; "
                            + "skipped to avoid an ambiguous update."
                    )
                );
                continue;
            }

            var match = matches[0];
            if (match.Id is { } cid)
                matchedLiveIds.Add(cid);
            Classify(kind, d, match, idMismatch: d.Id != match.Id, changed, ref unchanged);
        }

        // ── Any live entity we never matched is a prune candidate. ───────────────
        var removed = current
            .Where(e => e.Id is null || !matchedLiveIds.Contains(e.Id.Value))
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
    /// carries the desired body (for apply) and the live id (the update target). Body comparison
    /// uses <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/> — order-insensitive for
    /// object members, order-sensitive for arrays — so two <c>GET</c> bodies of the same
    /// unchanged entity compare equal regardless of the field order the server emitted.
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
        if (JsonNode.DeepEquals(desired.Body, current.Body))
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
}
