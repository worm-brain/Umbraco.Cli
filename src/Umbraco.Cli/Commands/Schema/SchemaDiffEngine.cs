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
    /// <returns>The diff across every schema kind.</returns>
    public static SchemaDiff Compare(SchemaSnapshot desired, SchemaSnapshot current) =>
        new(
            CompareKind(
                SchemaKinds.DocumentType,
                "alias",
                desired.DocumentTypes,
                current.DocumentTypes
            ),
            // Media and member types carry an alias, exactly as document types do.
            CompareKind(SchemaKinds.MediaType, "alias", desired.MediaTypes, current.MediaTypes),
            CompareKind(SchemaKinds.MemberType, "alias", desired.MemberTypes, current.MemberTypes),
            CompareKind(SchemaKinds.DataType, "name", desired.DataTypes, current.DataTypes),
            CompareKind(SchemaKinds.Template, "alias", desired.Templates, current.Templates)
        )
        {
            // #227. Languages have no id at all, so they match on the ISO code alone.
            Languages = CompareKind(
                SchemaKinds.Language,
                "isoCode",
                desired.Languages,
                current.Languages,
                live =>
                    Flag(live, "isDefault", true)
                        ? "The default language cannot be deleted; make another language the default first."
                        : null
            ),
            DictionaryItems = CompareKind(
                SchemaKinds.DictionaryItem,
                "name",
                desired.DictionaryItems,
                current.DictionaryItems
            ),
            MemberGroups = CompareKind(
                SchemaKinds.MemberGroup,
                "name",
                desired.MemberGroups,
                current.MemberGroups
            ),
            UserGroups = CompareKind(
                SchemaKinds.UserGroup,
                "alias",
                desired.UserGroups,
                current.UserGroups,
                live =>
                    Flag(live, "isDeletable", false)
                        ? "Umbraco does not allow this user group to be deleted."
                        : null
            ),
        };

    /// <summary>An entity reduced to what matching needs: its id, human key, and raw body.</summary>
    private readonly record struct Entry(Guid? Id, string Key, JsonNode Body);

    /// <summary>Diffs one entity kind by the GUID-primary, alias-fallback rules (two passes).</summary>
    /// <param name="kind">The entity-kind tag for the resulting changes.</param>
    /// <param name="keyField">The JSON field holding the human key (<c>alias</c>, <c>name</c> or <c>isoCode</c>).</param>
    /// <param name="desiredBodies">The desired entities' raw bodies.</param>
    /// <param name="currentBodies">The live entities' raw bodies.</param>
    /// <param name="undeletable">
    /// Says why a live entity can never be deleted (the default language, a built-in user group),
    /// or null. An unmatched live entity with a reason is <see cref="SchemaChangeKind.Skipped"/>
    /// with that note rather than <see cref="SchemaChangeKind.Removed"/>, so prune never tries it.
    /// </param>
    /// <returns>The diff for this kind.</returns>
    private static SchemaKindDiff CompareKind(
        string kind,
        string keyField,
        IReadOnlyList<JsonNode> desiredBodies,
        IReadOnlyList<JsonNode> currentBodies,
        Func<JsonNode, string?>? undeletable = null
    )
    {
        var desired = desiredBodies.Select(b => ToEntry(b, keyField)).ToList();
        var current = currentBodies.Select(b => ToEntry(b, keyField)).ToList();

        var added = new List<SchemaEntityChange>();
        var changed = new List<SchemaEntityChange>();
        var skipped = new List<SchemaEntityChange>();
        var unchanged = 0;

        // Live entities we have paired to a desired entity (or deliberately protected from
        // prune), by index: an entity with no id (a language, #227) must count as matched too, or
        // prune would delete what the snapshot just matched by key.
        var matchedLive = new HashSet<int>();
        var currentById = current
            .Select((e, i) => (e, i))
            .Where(x => x.e.Id is not null)
            .ToDictionary(x => x.e.Id!.Value, x => x.i);

        // ── Pass 1: GUID-primary. An exact id match is unambiguous. ──────────────
        var unmatchedDesired = new List<Entry>();
        foreach (var d in desired)
        {
            if (d.Id is { } did && currentById.TryGetValue(did, out var liveIndex))
            {
                matchedLive.Add(liveIndex);
                Classify(kind, d, current[liveIndex], idMismatch: false, changed, ref unchanged);
            }
            else
            {
                unmatchedDesired.Add(d);
            }
        }

        // ── Pass 2: alias/name fallback over the live entities NOT already id-matched. ──
        var unmatchedByKey = Enumerable
            .Range(0, current.Count)
            .Where(i => !string.IsNullOrEmpty(current[i].Key) && !matchedLive.Contains(i))
            .GroupBy(i => current[i].Key)
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
                matchedLive.UnionWith(matches);
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

            var match = current[matches[0]];
            matchedLive.Add(matches[0]);
            Classify(kind, d, match, idMismatch: d.Id != match.Id, changed, ref unchanged);
        }

        // ── Any live entity we never matched is a prune candidate, unless it can never be
        // deleted, which is reported as skipped with the reason instead. ─────────
        var removed = new List<SchemaEntityChange>();
        for (var i = 0; i < current.Count; i++)
        {
            if (matchedLive.Contains(i))
                continue;
            var e = current[i];
            if (undeletable?.Invoke(e.Body) is { } reason)
                skipped.Add(
                    new SchemaEntityChange(
                        kind,
                        SchemaChangeKind.Skipped,
                        e.Key,
                        DesiredId: null,
                        CurrentId: e.Id,
                        Note: reason
                    )
                );
            else
                removed.Add(
                    new SchemaEntityChange(
                        kind,
                        SchemaChangeKind.Removed,
                        e.Key,
                        DesiredId: null,
                        CurrentId: e.Id
                    )
                    {
                        CurrentBody = e.Body,
                    }
                );
        }

        return new SchemaKindDiff(added, changed, removed, skipped, unchanged);
    }

    /// <summary>Whether <paramref name="body"/>[<paramref name="field"/>] is the boolean <paramref name="value"/>.</summary>
    /// <param name="body">The entity body.</param>
    /// <param name="field">The boolean field.</param>
    /// <param name="value">The value to test for.</param>
    /// <returns>True when the field is present and equals <paramref name="value"/>.</returns>
    private static bool Flag(JsonNode body, string field, bool value) =>
        body[field] is JsonValue v && v.TryGetValue<bool>(out var b) && b == value;

    /// <summary>
    /// Classifies a matched desired/live pair as Changed (bodies differ) or Unchanged. A change
    /// carries the desired body (for apply), the live id (the update target) and the paths that
    /// differ. <see cref="JsonPathDiff"/> is empty exactly when the bodies are
    /// <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/> — order-insensitive for object
    /// members, order-sensitive for arrays — so two <c>GET</c> bodies of the same unchanged entity
    /// compare equal regardless of the field order the server emitted.
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
        var changes = JsonPathDiff.Paths(desired.Body, current.Body);
        if (changes.Count == 0)
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
                CurrentBody = current.Body,
                Changes = changes,
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
