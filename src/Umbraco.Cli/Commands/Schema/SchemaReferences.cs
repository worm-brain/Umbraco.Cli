using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Where a kind's bodies reference another schema item by id (#198), so a hand-written snapshot
/// can name it instead: <c>"dataType": "Textstring"</c> or <c>{ "id": "Textstring" }</c> where an
/// export writes <c>{ "id": "0cc0eba1-..." }</c>.
/// </summary>
/// <param name="Array">The body array whose elements hold the reference, or null for a top-level field.</param>
/// <param name="Field">
/// The field holding the reference: on the body when <paramref name="Array"/> is null, else on each
/// element. Null when each element of <paramref name="Array"/> is itself the reference.
/// </param>
/// <param name="Kind">What the reference names.</param>
public sealed record SchemaReference(string? Array, string? Field, EntityKind Kind)
{
    /// <summary>
    /// For an <see cref="Array"/> of objects that hold the reference in <see cref="Field"/>: builds
    /// the whole element from a reference given in place of the element (a bare name, a bare id,
    /// or <c>{ "id": ... }</c>) and the element's index, e.g.
    /// <c>"allowedDocumentTypes": ["blogPost"]</c> becomes
    /// <c>[{ "documentType": { "id": "..." }, "sortOrder": 0 }]</c> (#357). Null when the elements
    /// must be written out in full; a bare reference there is refused before anything is written.
    /// </summary>
    public Func<JsonObject, int, JsonObject>? FromBare { get; init; }
}

/// <summary>
/// Makes a hand-written snapshot workable before it is diffed (#198). A snapshot from
/// <c>schema export</c> passes through unchanged: every id it needs is already there. For one
/// written or edited by hand, three things the author would otherwise do by hand are done here:
/// <list type="number">
/// <item><b>Ids for new entries.</b> An entity, property or container without an <c>id</c> takes
/// the id of the live one it matches (an entity by its key, a property by alias, a container by
/// type, name and parent), or a new one. Reusing the live id matters: a property sent with a new
/// id is a new property, and the old one and its values would be dropped.</item>
/// <item><b>References by name.</b> Every reference in <see cref="SchemaKindSpec.References"/> may
/// name its target (alias, then name, ignoring case) instead of giving its id: the snapshot's own
/// entries are looked in first, so a snapshot can create a data type and use it, then the live
/// instance through the shared resolver (the rules every <c>&lt;id&gt;</c> argument uses).</item>
/// <item><b>Containers by name.</b> A property's <c>container</c> and a container's <c>parent</c>
/// may name a container of the same body, by name or by <c>Tab/Group</c> path.</item>
/// </list>
/// A reference that names nothing, or more than one thing, is an error naming where it is; the
/// snapshot is never applied on a guess.
/// </summary>
public static class SchemaReferences
{
    /// <summary>Normalises <paramref name="desired"/> in place, against the live instance.</summary>
    /// <param name="desired">The snapshot to normalise; changed in place.</param>
    /// <param name="live">The live export of (at least) every kind the snapshot manages.</param>
    /// <param name="client">The client, to resolve references the snapshot does not hold.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a failed resolver call (the instance could not be asked).</returns>
    /// <exception cref="InvalidInputException">A reference names no item, or more than one.</exception>
    public static async Task<UmbracoResponse<Empty>> NormaliseAsync(
        SchemaSnapshot desired,
        SchemaSnapshot live,
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        // Ids first, so references to the snapshot's own entries can be resolved to them.
        foreach (var kind in SchemaKinds.All.Where(k => k.Entity is not null))
            if (kind.Section(desired) is { } entries)
                FillEntityIds(kind, entries, kind.Section(live) ?? []);

        foreach (var kind in SchemaKinds.All.Where(k => k.HasProperties))
        foreach (var body in (kind.Section(desired) ?? []).OfType<JsonObject>())
            NormaliseProperties(kind, body, LiveMatch(kind, body, kind.Section(live) ?? []));

        var resolver = new Resolver(desired, client);
        try
        {
            foreach (var kind in SchemaKinds.All.Where(k => k.References.Count > 0))
            foreach (var body in (kind.Section(desired) ?? []).OfType<JsonObject>())
            foreach (var reference in kind.References)
                await ResolveReferencesAsync(kind, body, reference, resolver, ct);
        }
        catch (ResolveFailedException failed)
        {
            return failed.Response;
        }
        return UmbracoResponse<Empty>.Success(Empty.Value);
    }

    /// <summary>
    /// Gives every entry without an id the id of the one live entry with the same key (exactly as
    /// the diff matches), or a new id when there is none. An entry whose key matches several live
    /// entries is left without one, and the diff skips it as ambiguous.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="entries">The snapshot's entries.</param>
    /// <param name="live">The live entries.</param>
    private static void FillEntityIds(
        SchemaKindSpec kind,
        List<JsonNode> entries,
        List<JsonNode> live
    )
    {
        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (GuidOf(entry["id"]) is not null)
                continue;
            var key = Text(entry[kind.KeyField]);
            var matches = live.Where(l => key is not null && Text(l[kind.KeyField]) == key)
                .ToList();
            if (matches.Count > 1)
                continue;
            entry["id"] = (
                matches.Count == 1 ? GuidOf(matches[0]["id"]) ?? Guid.NewGuid() : Guid.NewGuid()
            ).ToString();
        }
    }

    /// <summary>The live entity a snapshot body matches: by id, else by its key when that is unique.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="body">The snapshot body.</param>
    /// <param name="live">The live entries.</param>
    /// <returns>The live body, or null.</returns>
    private static JsonObject? LiveMatch(SchemaKindSpec kind, JsonObject body, List<JsonNode> live)
    {
        var id = GuidOf(body["id"]);
        var byId = live.OfType<JsonObject>()
            .FirstOrDefault(l => id is not null && GuidOf(l["id"]) == id);
        if (byId is not null)
            return byId;
        var key = Text(body[kind.KeyField]);
        var byKey = live.OfType<JsonObject>()
            .Where(l => key is not null && Text(l[kind.KeyField]) == key)
            .ToList();
        return byKey.Count == 1 ? byKey[0] : null;
    }

    /// <summary>
    /// Completes a type body's containers and properties: container ids (the matching live
    /// container's, else new), container references by name, and property ids (the live property
    /// with the same alias, else new).
    /// </summary>
    /// <param name="kind">The type kind.</param>
    /// <param name="body">The snapshot body; changed in place.</param>
    /// <param name="liveBody">The live type it matches, or null for a new type.</param>
    /// <exception cref="InvalidInputException">A container reference names no container, or several.</exception>
    private static void NormaliseProperties(
        SchemaKindSpec kind,
        JsonObject body,
        JsonObject? liveBody
    )
    {
        var where = $"{kind.Tag} '{Text(body[kind.KeyField])}'";
        var containers = (body["containers"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var liveContainers = (liveBody?["containers"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .ToList();

        // ── Container ids, matched to the live container of the same type, name and parent. ──
        foreach (var container in containers.Where(c => GuidOf(c["id"]) is null))
        {
            var candidates = liveContainers
                .Where(l =>
                    SameText(l["type"], container["type"]) && SameText(l["name"], container["name"])
                )
                .ToList();
            if (candidates.Count > 1)
            {
                var parent = ParentName(container, containers);
                candidates =
                [
                    .. candidates.Where(l =>
                        string.Equals(
                            ParentName(l, liveContainers),
                            parent,
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),
                ];
            }
            container["id"] = (
                candidates.Count == 1
                    ? GuidOf(candidates[0]["id"]) ?? Guid.NewGuid()
                    : Guid.NewGuid()
            ).ToString();
        }

        // ── Container references by name or Tab/Group path, within this body. ──
        foreach (var container in containers)
            if (NameIn(container["parent"]) is { } name)
                container["parent"] = IdRef(
                    FindContainer(
                        name,
                        containers,
                        $"{where}: container '{Text(container["name"])}' parent"
                    )
                );

        var liveProperties = (liveBody?["properties"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .ToList();
        foreach (var property in (body["properties"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (NameIn(property["container"]) is { } name)
                property["container"] = IdRef(
                    FindContainer(
                        name,
                        containers,
                        $"{where}: property '{Text(property["alias"])}' container"
                    )
                );

            // ── Property ids: the live property with the same alias keeps its id, and its values. ──
            if (GuidOf(property["id"]) is null)
            {
                var alias = Text(property["alias"]);
                var match = liveProperties.FirstOrDefault(l =>
                    alias is not null
                    && string.Equals(Text(l["alias"]), alias, StringComparison.OrdinalIgnoreCase)
                );
                property["id"] = (GuidOf(match?["id"]) ?? Guid.NewGuid()).ToString();
            }
        }
    }

    /// <summary>
    /// The container a name or <c>Tab/Group</c> path names among a body's containers: a full path
    /// match first, then a bare name match, ignoring case.
    /// </summary>
    /// <param name="name">The name or path.</param>
    /// <param name="containers">The body's containers (with ids).</param>
    /// <param name="where">Where the reference is, for the error.</param>
    /// <returns>The container id.</returns>
    /// <exception cref="InvalidInputException">It names no container, or several.</exception>
    private static Guid FindContainer(string name, List<JsonObject> containers, string where)
    {
        var byPath = containers
            .Where(c =>
                string.Equals(PathOf(c, containers), name, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        var matches =
            byPath.Count > 0
                ? byPath
                :
                [
                    .. containers.Where(c =>
                        string.Equals(Text(c["name"]), name, StringComparison.OrdinalIgnoreCase)
                    ),
                ];
        return matches.Count switch
        {
            1 => GuidOf(matches[0]["id"])!.Value,
            0 => throw new InvalidInputException(
                $"{where} names '{name}', which is not one of this type's containers."
            ),
            _ => throw new InvalidInputException(
                $"{where} names '{name}', which matches {matches.Count} containers. Use its "
                    + "'Tab/Group' path or its id."
            ),
        };
    }

    /// <summary>A container's path: its parent's path, a slash and its name (<c>Content/Hero</c>).</summary>
    /// <param name="container">The container.</param>
    /// <param name="containers">The body's containers.</param>
    /// <returns>The path.</returns>
    private static string PathOf(JsonObject container, List<JsonObject> containers)
    {
        // Umbraco nests at most tab > group; the depth bound only stops a hand-made cycle.
        var names = new List<string>();
        JsonObject? c = container;
        for (var depth = 0; c is not null && depth < 8; depth++)
        {
            names.Insert(0, Text(c["name"]) ?? "");
            c = ParentOf(c, containers);
        }
        return string.Join('/', names);
    }

    /// <summary>A container's parent container among <paramref name="containers"/>, by id or by name.</summary>
    /// <param name="container">The container.</param>
    /// <param name="containers">The body's containers.</param>
    /// <returns>The parent, or null at the top or when it cannot be told.</returns>
    private static JsonObject? ParentOf(JsonObject container, List<JsonObject> containers)
    {
        var parent = container["parent"];
        if (GuidOf(parent is JsonObject o ? o["id"] : parent) is { } id)
            return containers.FirstOrDefault(c => GuidOf(c["id"]) == id);
        // A parent named by name: a container is never its own parent, so a group named like its
        // tab ("Content" in "Content") finds the tab.
        return NameIn(parent) is { } name
            ? containers.FirstOrDefault(c =>
                !ReferenceEquals(c, container)
                && string.Equals(
                    Text(c["name"]),
                    name.Split('/')[^1],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            : null;
    }

    /// <summary>The name of a container's parent, for matching a new container to a live one.</summary>
    /// <param name="container">The container.</param>
    /// <param name="containers">The containers it sits among.</param>
    /// <returns>The parent's name, or null at the top.</returns>
    private static string? ParentName(JsonObject container, List<JsonObject> containers) =>
        NameIn(container["parent"]) is { } name
            ? name.Split('/')[^1]
            : Text(ParentOf(container, containers)?["name"]);

    /// <summary>Rewrites one reference location of a body to ids.</summary>
    /// <param name="kind">The body's kind.</param>
    /// <param name="body">The body; changed in place.</param>
    /// <param name="reference">Where the reference sits and what it names.</param>
    /// <param name="resolver">The name resolver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the location is rewritten.</returns>
    private static async Task ResolveReferencesAsync(
        SchemaKindSpec kind,
        JsonObject body,
        SchemaReference reference,
        Resolver resolver,
        CancellationToken ct
    )
    {
        // Only a reference that changes is written back: a field the body does not have stays
        // absent, so an exported body is left exactly as it is.
        var where = $"{kind.Tag} '{Text(body[kind.KeyField])}'";
        if (reference.Array is null)
        {
            var field = reference.Field!;
            if (
                await RewriteAsync(
                    body[field],
                    reference.Kind,
                    $"{where}: {field}",
                    resolver,
                    ct
                ) is
                { } replaced
            )
                body[field] = replaced;
            return;
        }
        if (body[reference.Array] is not JsonArray array)
            return;
        for (var i = 0; i < array.Count; i++)
        {
            if (reference.Field is null)
            {
                if (
                    await RewriteAsync(
                        array[i],
                        reference.Kind,
                        $"{where}: {reference.Array}[{i}]",
                        resolver,
                        ct
                    ) is
                    { } item
                )
                    array[i] = item;
                continue;
            }
            if (IsBareReference(array[i], reference))
            {
                array[i] = await ExpandBareAsync(
                    array[i]!,
                    i,
                    reference,
                    $"{where}: {reference.Array}[{i}]",
                    resolver,
                    ct
                );
                continue;
            }
            if (
                array[i] is JsonObject element
                && await RewriteAsync(
                    element[reference.Field],
                    reference.Kind,
                    $"{where}: {reference.Array}[{Text(element["alias"]) ?? i.ToString()}].{reference.Field}",
                    resolver,
                    ct
                )
                    is { } value
            )
                element[reference.Field] = value;
        }
    }

    /// <summary>
    /// Whether an array element is a reference given in place of the object that should hold it
    /// in <paramref name="reference"/>'s field: any bare value (a string, or a number that can
    /// never be right), or, where the location builds elements from a bare reference, an object
    /// with an <c>id</c> and no such field. Elsewhere an object with an <c>id</c> is an element of
    /// its own (a property has its own id), so it is left to the normal path, as are null elements.
    /// </summary>
    /// <param name="element">The array element.</param>
    /// <param name="reference">The reference location.</param>
    /// <returns>True when the element must be expanded, or refused.</returns>
    private static bool IsBareReference(JsonNode? element, SchemaReference reference) =>
        element is JsonValue
        || reference.FromBare is not null
            && element is JsonObject o
            && !o.ContainsKey(reference.Field!)
            && o.ContainsKey("id");

    /// <summary>
    /// Replaces a bare reference element with the full element the API takes, through
    /// <see cref="SchemaReference.FromBare"/>, resolving a name first. This runs in the pre-write
    /// pass, so a shape the API would refuse fails the diff or apply before anything is written,
    /// rather than halfway through an apply (#357).
    /// </summary>
    /// <param name="element">The bare element.</param>
    /// <param name="index">Its index in the array.</param>
    /// <param name="reference">The reference location, with its element builder.</param>
    /// <param name="where">Where the element is, for errors.</param>
    /// <param name="resolver">The name resolver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full element.</returns>
    /// <exception cref="InvalidInputException">
    /// The location takes no bare reference, the element is neither a name nor an id, or a name
    /// resolves to nothing or to several items.
    /// </exception>
    private static async Task<JsonNode> ExpandBareAsync(
        JsonNode element,
        int index,
        SchemaReference reference,
        string where,
        Resolver resolver,
        CancellationToken ct
    )
    {
        var isReference =
            NameIn(element) is not null
            || GuidOf(element is JsonObject o ? o["id"] : element) is not null;
        if (reference.FromBare is null || !isReference)
            throw new InvalidInputException(
                $"{where} must be an object with a '{reference.Field}' field naming the "
                    + $"{reference.Kind.Noun()}, e.g. {{ \"{reference.Field}\": \"<alias or id>\" }}."
            );
        // A name is resolved and a bare id wrapped; an { "id": "<guid>" } is already the shape.
        var idRef = (JsonObject)(
            await RewriteAsync(element, reference.Kind, where, resolver, ct) ?? element.DeepClone()
        );
        return reference.FromBare(idRef, index);
    }

    /// <summary>
    /// A reference in the shape the API takes, <c>{ "id": ... }</c>: a name is resolved and a bare
    /// id string is wrapped. Anything else (an id reference, null, a missing field) needs no
    /// change.
    /// </summary>
    /// <param name="node">The reference as written.</param>
    /// <param name="kind">What it names.</param>
    /// <param name="where">Where it is, for errors.</param>
    /// <param name="resolver">The name resolver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The node to write back, or null when the reference needs no change.</returns>
    private static async Task<JsonNode?> RewriteAsync(
        JsonNode? node,
        EntityKind kind,
        string where,
        Resolver resolver,
        CancellationToken ct
    )
    {
        if (NameIn(node) is { } name)
            return IdRef(await resolver.ResolveAsync(kind, name, where, ct));
        // A bare id string where the API wants an object.
        return node is JsonValue && GuidOf(node) is { } id ? IdRef(id) : null;
    }

    /// <summary>The name a reference gives instead of an id: a non-id string, bare or as <c>{ "id": ... }</c>.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The name, or null when the reference is an id, null, or not a reference.</returns>
    private static string? NameIn(JsonNode? reference) =>
        Text(reference is JsonObject o ? o["id"] : reference) is { Length: > 0 } s
        && !Guid.TryParse(s, out _)
            ? s
            : null;

    /// <summary>A reference object naming <paramref name="id"/>.</summary>
    /// <param name="id">The id.</param>
    /// <returns><c>{ "id": "..." }</c>.</returns>
    private static JsonObject IdRef(Guid id) => new() { ["id"] = id.ToString() };

    /// <summary>A node's string value, or null when it is not a string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The string, or null.</returns>
    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A node's value as an id, or null when it is not an id string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id, or null.</returns>
    private static Guid? GuidOf(JsonNode? node) =>
        Text(node) is { } s && Guid.TryParse(s, out var id) ? id : null;

    /// <summary>Whether two nodes hold the same string, ignoring case.</summary>
    /// <param name="a">One node.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when both are strings and equal ignoring case.</returns>
    private static bool SameText(JsonNode? a, JsonNode? b) =>
        Text(a) is { } x && string.Equals(x, Text(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves names to ids: the snapshot's own entries of the kind first (by the kind's key, then
    /// name, ignoring case), then the live instance through the shared resolver. Answers are
    /// cached, so a data type named by twenty properties is looked up once.
    /// </summary>
    /// <param name="desired">The snapshot.</param>
    /// <param name="client">The client.</param>
    private sealed class Resolver(SchemaSnapshot desired, IUmbracoManagementClient client)
    {
        /// <summary>The answers so far.</summary>
        private readonly Dictionary<(EntityKind, string), Guid> _cache = [];

        /// <summary>Resolves one name.</summary>
        /// <param name="kind">What it names.</param>
        /// <param name="name">The name.</param>
        /// <param name="where">Where it is, for errors.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The id.</returns>
        /// <exception cref="InvalidInputException">It names no item, or several.</exception>
        /// <exception cref="ResolveFailedException">The instance could not be asked.</exception>
        public async Task<Guid> ResolveAsync(
            EntityKind kind,
            string name,
            string where,
            CancellationToken ct
        )
        {
            if (_cache.TryGetValue((kind, name.ToLowerInvariant()), out var cached))
                return cached;

            var (snapshotId, byKey) = FromSnapshot(kind, name, where);
            var id = snapshotId switch
            {
                null => await FromInstanceAsync(kind, name, where, ct),
                // The snapshot's key (an alias, or a data type's name) is the entry's own identity.
                _ when byKey => snapshotId.Value,
                // Only a snapshot entry's display name matched: the instance may hold something
                // else under that alias or name (#359).
                _ => await CheckNameMatchAsync(kind, name, where, snapshotId.Value, ct),
            };
            _cache[(kind, name.ToLowerInvariant())] = id;
            return id;
        }

        /// <summary>
        /// Checks a reference that matched a snapshot entry by its display name only against the
        /// instance: when the instance resolves the same reference (by alias, then name) to an item
        /// the snapshot does not hold, the reference names two things and is refused, rather than
        /// the snapshot's name silently shadowing a live alias (#359). A live item the snapshot
        /// also holds is described by the snapshot, so it does not count.
        /// </summary>
        /// <param name="kind">What it names.</param>
        /// <param name="name">The name.</param>
        /// <param name="where">Where it is, for errors.</param>
        /// <param name="snapshotId">The id of the snapshot entry the name matched.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><paramref name="snapshotId"/>, when the reference is not ambiguous.</returns>
        /// <exception cref="InvalidInputException">The instance holds another item it names.</exception>
        /// <exception cref="ResolveFailedException">The instance could not be asked.</exception>
        private async Task<Guid> CheckNameMatchAsync(
            EntityKind kind,
            string name,
            string where,
            Guid snapshotId,
            CancellationToken ct
        )
        {
            var live = await client.ResolveIdAsync(kind, name, ct);
            if (!live.IsSuccess)
                return IsBadName(live)
                    ? snapshotId
                    : throw new ResolveFailedException(UmbracoResponse<Empty>.FailureFrom(live));
            if (live.Data == snapshotId || SnapshotIds(kind).Contains(live.Data))
                return snapshotId;
            throw new InvalidInputException(
                $"{where} names {kind.Noun()} '{name}', which is ambiguous: it is the name of the "
                    + $"snapshot's entry {snapshotId}, and on the instance it names {live.Data}. "
                    + "Use the alias or the id of the one you mean."
            );
        }

        /// <summary>The ids of the snapshot's entries of a kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The ids.</returns>
        private HashSet<Guid> SnapshotIds(EntityKind kind) =>
            [
                .. (SchemaKinds.All.FirstOrDefault(k => k.Entity == kind)?.Section(desired) ?? [])
                    .Select(e => GuidOf(e["id"]))
                    .OfType<Guid>(),
            ];

        /// <summary>
        /// Whether a failed resolver call is a bad name (nothing, or several items, match) rather
        /// than an instance that could not be asked. The real resolver reports it as
        /// invalid_argument with no HTTP status (GuardedApiAsync); a 404/409 is the same answer.
        /// </summary>
        /// <param name="resolved">The failed call.</param>
        /// <returns>True for a bad name.</returns>
        private static bool IsBadName(UmbracoResponse<Guid> resolved) =>
            resolved.Category == FailureCategory.InvalidArgument
            || resolved.StatusCode is 404 or 409;

        /// <summary>
        /// The id of the snapshot entry the name matches, by the kind's key first, then by name.
        /// </summary>
        /// <param name="kind">What it names.</param>
        /// <param name="name">The name.</param>
        /// <param name="where">Where it is, for errors.</param>
        /// <returns>
        /// The id, or null when no entry matches; and whether it matched by the kind's key rather
        /// than by display name.
        /// </returns>
        /// <exception cref="InvalidInputException">It matches several entries, or one with no id.</exception>
        private (Guid? Id, bool ByKey) FromSnapshot(EntityKind kind, string name, string where)
        {
            var spec = SchemaKinds.All.FirstOrDefault(k => k.Entity == kind);
            var entries = (spec?.Section(desired) ?? []).OfType<JsonObject>().ToList();
            List<JsonObject> Matching(string field) =>
                [
                    .. entries.Where(e =>
                        string.Equals(Text(e[field]), name, StringComparison.OrdinalIgnoreCase)
                    ),
                ];
            var matches = Matching(spec?.KeyField ?? "name");
            var byKey = matches.Count > 0;
            if (!byKey)
                matches = Matching("name");
            if (matches.Count == 0)
                return (null, false);
            if (matches.Count > 1)
                throw new InvalidInputException(
                    $"{where} names {kind.Noun()} '{name}', which matches {matches.Count} "
                        + $"entries in the snapshot. Use its id."
                );
            // An entry is left without an id only when its key matches several live items.
            return (
                GuidOf(matches[0]["id"])
                    ?? throw new InvalidInputException(
                        $"{where} names {kind.Noun()} '{name}', which matches more than one "
                            + $"{kind.Noun()} on the instance. Use its id."
                    ),
                byKey
            );
        }

        /// <summary>The id of the live item the name matches.</summary>
        /// <param name="kind">What it names.</param>
        /// <param name="name">The name.</param>
        /// <param name="where">Where it is, for errors.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The id.</returns>
        /// <exception cref="InvalidInputException">It names no live item, or several.</exception>
        /// <exception cref="ResolveFailedException">The instance could not be asked.</exception>
        private async Task<Guid> FromInstanceAsync(
            EntityKind kind,
            string name,
            string where,
            CancellationToken ct
        )
        {
            var resolved = await client.ResolveIdAsync(kind, name, ct);
            if (resolved.IsSuccess)
                return resolved.Data;
            // A bad name in the snapshot, never an unreachable instance.
            if (IsBadName(resolved))
                throw new InvalidInputException(
                    $"{where} names {kind.Noun()} '{name}', which is not in the snapshot, and the "
                        + $"instance answered: {resolved.ErrorMessage}"
                );
            throw new ResolveFailedException(UmbracoResponse<Empty>.FailureFrom(resolved));
        }
    }

    /// <summary>Carries a failed resolver call (not a bad name) out of the walk.</summary>
    /// <param name="response">The failure.</param>
    private sealed class ResolveFailedException(UmbracoResponse<Empty> response) : Exception
    {
        /// <summary>The failure to return.</summary>
        public UmbracoResponse<Empty> Response { get; } = response;
    }
}
