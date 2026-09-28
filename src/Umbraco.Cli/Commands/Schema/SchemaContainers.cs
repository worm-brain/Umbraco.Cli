using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Matches a snapshot type's containers (tabs and groups) to the target's across instances
/// (#397). A snapshot exported from another instance keeps that instance's container ids, so
/// without this the diff pairs no container by id (#384) and apply writes the source ids. A
/// container whose id is not on the target takes the id of the one target container with the same
/// type, name and parent name; the references to it (a group's <c>parent</c>, a property's
/// <c>container</c>) follow. A snapshot applied to the instance it came from carries the target's
/// ids already, so nothing changes.
/// </summary>
public static class SchemaContainers
{
    /// <summary>
    /// Returns <paramref name="desired"/> with its containers matched to <paramref name="live"/>'s
    /// by type, name and parent where their ids differ, or the reason it cannot be matched without
    /// a guess. The desired body is not changed; a body with no containers comes back as it is.
    /// </summary>
    /// <param name="desired">The snapshot's type body.</param>
    /// <param name="live">The target's body of the same type.</param>
    /// <returns>
    /// The body to diff and apply, with a null <c>Ambiguity</c>; or the unchanged body with a note
    /// naming each container that matches several target containers, and their ids.
    /// </returns>
    public static (JsonNode Body, string? Ambiguity) MatchToLive(JsonNode desired, JsonNode live)
    {
        var mine = Containers(desired);
        var theirs = Containers(live);
        if (mine.Count == 0 || theirs.Count == 0)
            return (desired, null);

        var liveIds = theirs.Select(c => IdOf(c["id"])).OfType<string>().ToHashSet(IgnoreCase);
        // A target container the snapshot already names by id is taken, and never a match for
        // another snapshot container.
        var taken = mine.Select(c => IdOf(c["id"]))
            .OfType<string>()
            .Where(liveIds.Contains)
            .ToHashSet(IgnoreCase);

        // ── Work out every match first, on the snapshot's own ids, so each parent name is read
        // before any id is rewritten. ──
        var map = new Dictionary<string, string>(IgnoreCase);
        var ambiguities = new List<string>();
        foreach (var container in mine)
        {
            if (IdOf(container["id"]) is not { } id || liveIds.Contains(id))
                continue;
            var parent = ParentName(container, mine);
            var candidates = theirs
                .Where(l =>
                    IdOf(l["id"]) is { } lid
                    && !taken.Contains(lid)
                    && SameText(l["type"], container["type"])
                    && SameText(l["name"], container["name"])
                    && IgnoreCase.Equals(ParentName(l, theirs), parent)
                )
                .ToList();
            if (candidates.Count == 1)
            {
                var match = IdOf(candidates[0]["id"])!;
                map[id] = match;
                taken.Add(match);
            }
            else if (candidates.Count > 1)
                ambiguities.Add(
                    $"container '{Text(container["name"])}' ({Text(container["type"])}) matches "
                        + $"{candidates.Count} target containers by type, name and parent "
                        + $"({string.Join(", ", candidates.Select(c => IdOf(c["id"])))})"
                );
            // No candidate: a new container, created with the snapshot's id as before.
        }

        if (ambiguities.Count > 0)
            return (
                desired,
                $"Its {string.Join("; ", ambiguities)}. Set the container's id to the one meant; "
                    + "skipped to avoid a guess."
            );
        if (map.Count == 0)
            return (desired, null);

        // ── Rewrite the ids and every reference to them, on a copy. ──
        var body = desired.DeepClone();
        foreach (var container in Containers(body))
        {
            Remap(container, "id", map);
            if (container["parent"] is JsonObject parentRef)
                Remap(parentRef, "id", map);
        }
        foreach (var property in (body["properties"] as JsonArray ?? []).OfType<JsonObject>())
            if (property["container"] is JsonObject containerRef)
                Remap(containerRef, "id", map);
        return (body, null);
    }

    /// <summary>Container ids are compared ignoring case, as GUID text may be written either way.</summary>
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    /// <summary>A body's container objects.</summary>
    /// <param name="body">The type body.</param>
    /// <returns>Its containers, or none.</returns>
    private static List<JsonObject> Containers(JsonNode body) =>
        (body["containers"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    /// <summary>Replaces <paramref name="field"/> of <paramref name="node"/> with its mapped id, when it has one.</summary>
    /// <param name="node">The object holding the id.</param>
    /// <param name="field">The id field.</param>
    /// <param name="map">Snapshot id to target id.</param>
    private static void Remap(JsonObject node, string field, Dictionary<string, string> map)
    {
        if (IdOf(node[field]) is { } id && map.TryGetValue(id, out var target))
            node[field] = target;
    }

    /// <summary>The name of a container's parent among <paramref name="containers"/>, or null at the top.</summary>
    /// <param name="container">The container.</param>
    /// <param name="containers">The containers of the same body.</param>
    /// <returns>The parent's name, or null.</returns>
    private static string? ParentName(JsonObject container, List<JsonObject> containers) =>
        IdOf((container["parent"] as JsonObject)?["id"]) is { } parentId
            ? Text(
                containers.FirstOrDefault(c => IgnoreCase.Equals(IdOf(c["id"]), parentId))?["name"]
            )
            : null;

    /// <summary>An id field's text, or null when it is missing or empty.</summary>
    /// <param name="node">The id node.</param>
    /// <returns>The id text, or null.</returns>
    private static string? IdOf(JsonNode? node) => Text(node) is { Length: > 0 } s ? s : null;

    /// <summary>A node's string value, or null when it is not a string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The string, or null.</returns>
    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Whether two nodes hold the same string, ignoring case.</summary>
    /// <param name="a">One node.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when both are strings and equal ignoring case.</returns>
    private static bool SameText(JsonNode? a, JsonNode? b) =>
        Text(a) is { } x && string.Equals(x, Text(b), StringComparison.OrdinalIgnoreCase);
}
