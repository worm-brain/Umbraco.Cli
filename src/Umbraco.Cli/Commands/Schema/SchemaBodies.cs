using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The per-kind body shaping the schema snapshot needs for the kinds added in #227. Every other
/// kind is stored verbatim (ADR 0005 §1); these two are not, for reasons the API forces:
/// <list type="bullet">
/// <item>A <b>dictionary item</b>'s read has no parent, so the snapshot adds it, and its translations
/// come back in no fixed order, so they are sorted to compare stably.</item>
/// <item>A <b>user group</b> names content on one instance (start nodes, per-document permissions),
/// which would point at nothing on another. The snapshot leaves those out, and an update keeps the
/// target's own.</item>
/// </list>
/// Export shapes both sides the same way, so diff compares like with like.
/// </summary>
public static class SchemaBodies
{
    /// <summary>The user group fields that name a content or media item on one instance.</summary>
    private static readonly string[] StartNodeFields = ["documentStartNode", "mediaStartNode"];

    /// <summary>
    /// A dictionary item's snapshot body: a copy of <paramref name="body"/> with <c>parent</c> set
    /// (<c>{"id": ...}</c>, or null at the root, the shape the create takes) and its
    /// <c>translations</c> sorted by ISO code.
    /// </summary>
    /// <param name="body">The item as <c>GET /dictionary/{id}</c> returns it.</param>
    /// <param name="parentId">The item's parent, or null at the root.</param>
    /// <returns>The shaped copy.</returns>
    public static JsonNode DictionaryItem(JsonNode body, Guid? parentId)
    {
        var clone = body.DeepClone();
        clone["parent"] = parentId is { } p ? new JsonObject { ["id"] = p.ToString() } : null;
        if (clone["translations"] is JsonArray translations)
            clone["translations"] = new JsonArray([
                .. translations
                    .OrderBy(t => t?["isoCode"]?.GetValue<string>(), StringComparer.Ordinal)
                    .Select(t => t?.DeepClone()),
            ]);
        return clone;
    }

    /// <summary>The parent id a dictionary snapshot body carries, or null at the root.</summary>
    /// <param name="body">A body shaped by <see cref="DictionaryItem"/>.</param>
    /// <returns>The parent id, or null.</returns>
    public static Guid? ParentOf(JsonNode? body) =>
        body?["parent"]?["id"] is JsonValue v
        && v.TryGetValue<string>(out var s)
        && Guid.TryParse(s, out var id)
            ? id
            : null;

    /// <summary>
    /// A copy of a dictionary body without its <c>parent</c>: the update takes only the name and
    /// translations, and a parent change is a separate move.
    /// </summary>
    /// <param name="body">A body shaped by <see cref="DictionaryItem"/>.</param>
    /// <returns>The copy.</returns>
    public static JsonNode WithoutParent(JsonNode body)
    {
        var clone = body.DeepClone();
        clone.AsObject().Remove("parent");
        return clone;
    }

    /// <summary>
    /// A user group's snapshot body: a copy of <paramref name="body"/> without the start nodes and
    /// without the permissions granted on one document (the <c>permissions</c> entries that carry a
    /// <c>document</c>). What is left - name, alias, sections, languages, general and
    /// property-value permissions, root access - means the same on any instance.
    /// </summary>
    /// <param name="body">The group as <c>GET /user-group/{id}</c> returns it.</param>
    /// <returns>The portable copy.</returns>
    public static JsonNode PortableUserGroup(JsonNode body)
    {
        var clone = body.DeepClone().AsObject();
        foreach (var field in StartNodeFields)
            clone.Remove(field);
        if (clone["permissions"] is JsonArray permissions)
            clone["permissions"] = new JsonArray([
                .. permissions.Where(p => !IsDocumentPermission(p)).Select(p => p?.DeepClone()),
            ]);
        return clone;
    }

    /// <summary>
    /// The body to write over a live user group: <paramref name="desired"/> (a portable snapshot
    /// body) with the live group's start nodes and per-document permissions put back, so an apply
    /// changes what the snapshot carries and leaves the instance-specific parts alone.
    /// </summary>
    /// <param name="desired">The snapshot body.</param>
    /// <param name="live">The live group, read in full.</param>
    /// <returns>The merged copy.</returns>
    public static JsonNode WithLiveNodes(JsonNode desired, JsonNode live)
    {
        var merged = desired.DeepClone().AsObject();
        foreach (var field in StartNodeFields)
            if (live[field] is { } value)
                merged[field] = value.DeepClone();

        var livePerDocument = (live["permissions"] as JsonArray ?? [])
            .Where(IsDocumentPermission)
            .Select(p => p?.DeepClone());
        var desiredPermissions = (merged["permissions"] as JsonArray ?? [])
            .Where(p => !IsDocumentPermission(p))
            .Select(p => p?.DeepClone());
        merged["permissions"] = new JsonArray([.. desiredPermissions, .. livePerDocument]);
        return merged;
    }

    /// <summary>Whether a user group permission is granted on one document.</summary>
    /// <param name="permission">A <c>permissions</c> entry.</param>
    /// <returns>True when it carries a <c>document</c>.</returns>
    private static bool IsDocumentPermission(JsonNode? permission) =>
        permission is JsonObject o && o["document"] is not null;
}
