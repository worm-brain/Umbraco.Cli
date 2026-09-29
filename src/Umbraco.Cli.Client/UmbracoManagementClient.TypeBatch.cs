using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// Reads document, media and member types and data types in batches (#418). The trees carry no
/// alias and no configuration, so every read that needs a type's alias or full body used to cost
/// one <c>GET /{kind}/{id}</c> per type. Umbraco 17.3 added <c>GET /{kind}/batch?id=..&amp;id=..</c>
/// for the four kinds, whose items are the same models the by-id reads return, so a chunk of
/// types now costs one request. The type trees are walked once per client (#415), and the lists
/// read aliases only for the page they return (#416).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Ids per batch request. 40 ids keep the query string at about 1.6 KB, under IIS's default
    /// <c>maxQueryString</c> of 2,048 bytes, which an Umbraco hosted on IIS or Azure App Service
    /// enforces.
    /// </summary>
    private const int TypeBatchSize = 40;

    /// <summary>
    /// Set once a batch request answers 404: Umbraco 17.0 to 17.2 have no batch endpoints, so
    /// from then on this client reads each type by id, as it did before #418, and the probe
    /// costs one request per run.
    /// </summary>
    private bool _typeBatchUnavailable;

    /// <summary>(kind, id) pairs whose alias has been read, so a type the server did not return is not asked for again.</summary>
    private readonly HashSet<(EntityKind, Guid)> _aliasesRead = [];

    /// <summary>Every document type (folders excluded), from one walk of the tree per client.</summary>
    private List<DocumentTypeResponse>? _documentTypeLeaves;

    /// <summary>Every media type (folders excluded), from one walk of the tree per client.</summary>
    private List<MediaTypeResponse>? _mediaTypeLeaves;

    /// <summary>Every member type (folders excluded), from one walk of the tree per client.</summary>
    private List<MemberTypeResponse>? _memberTypeLeaves;

    /// <summary>Every data type (folders excluded) with its tree parent, from one walk per client (#415).</summary>
    private List<DataTypeResponse>? _dataTypeLeaves;

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetSchemaRawManyAsync(
        EntityKind kind,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<JsonNode>>(
            ct,
            async () =>
            {
                var found = await ReadSchemaBodiesAsync(kind, ids, ct);
                var bodies = new List<JsonNode>(ids.Count);
                foreach (var id in ids)
                {
                    // An item the batch left out is read on its own, so it fails (a 404) exactly
                    // as the by-id read did before, and the export stops rather than drop it.
                    bodies.Add(
                        found.TryGetValue(id, out var body)
                            ? body
                            : await GetRawJsonAsync($"{ApiRoot}/{SchemaSegment(kind)}/{id}", ct)
                    );
                }
                return bodies;
            }
        );

    /// <summary>Whether Umbraco has a <c>/{kind}/batch</c> read for the kind.</summary>
    /// <param name="kind">The schema kind.</param>
    /// <returns>True for document, media and member types and data types.</returns>
    private static bool HasBatchEndpoint(EntityKind kind) =>
        kind
            is EntityKind.DocumentType
                or EntityKind.MediaType
                or EntityKind.MemberType
                or EntityKind.DataType;

    /// <summary>
    /// Reads items by id, <see cref="TypeBatchSize"/> per batch request, falling back to one read
    /// per id when the kind has no batch endpoint or the server has none (a 404).
    /// </summary>
    /// <typeparam name="T">The item model.</typeparam>
    /// <param name="ids">The ids to read.</param>
    /// <param name="readBatch">Reads one chunk through the batch endpoint; null when the kind has none.</param>
    /// <param name="readOne">Reads one item by id.</param>
    /// <param name="idOf">An item's id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The items found, by id. An id the server did not return, or whose by-id read failed, is
    /// missing: each caller decides what a missing item means.
    /// </returns>
    /// <exception cref="ApiException">A batch request failed with anything but a 404.</exception>
    private async Task<Dictionary<Guid, T>> ReadInBatchesAsync<T>(
        IReadOnlyCollection<Guid> ids,
        Func<Guid[], CancellationToken, Task<IEnumerable<T>?>>? readBatch,
        Func<Guid, CancellationToken, Task<T?>> readOne,
        Func<T, Guid?> idOf,
        CancellationToken ct
    )
        where T : class
    {
        var found = new Dictionary<Guid, T>();
        foreach (var chunk in ids.Distinct().Chunk(TypeBatchSize))
        {
            if (readBatch is not null && !_typeBatchUnavailable)
            {
                try
                {
                    foreach (var item in await readBatch(chunk, ct) ?? [])
                        if (idOf(item) is { } id)
                            found[id] = item;
                    continue;
                }
                catch (ApiException e) when (e.ResponseStatusCode == 404)
                {
                    _typeBatchUnavailable = true;
                }
            }

            foreach (var id in chunk)
            {
                try
                {
                    if (await readOne(id, ct) is { } item)
                        found[id] = item;
                }
                catch (ApiException)
                {
                    // Missing, as the batch would leave it out.
                }
            }
        }
        return found;
    }

    /// <summary>
    /// The verbatim bodies of schema items, by id, through <see cref="ReadInBatchesAsync{T}"/>:
    /// the batch endpoint's items, detached from their response so they can be placed in a
    /// snapshot.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="ids">The ids to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bodies found, by id; see <see cref="ReadInBatchesAsync{T}"/> for what is missing.</returns>
    private Task<Dictionary<Guid, JsonNode>> ReadSchemaBodiesAsync(
        EntityKind kind,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    )
    {
        var path = $"{ApiRoot}/{SchemaSegment(kind)}";
        return ReadInBatchesAsync<JsonNode>(
            ids,
            HasBatchEndpoint(kind)
                ? async (chunk, c) =>
                {
                    var query = string.Join("&", chunk.Select(id => $"id={id}"));
                    var items = (await GetRawJsonAsync($"{path}/batch?{query}", c))["items"];
                    if (items is not JsonArray array)
                        return [];
                    var list = array.OfType<JsonNode>().ToList();
                    array.Clear();
                    return list;
                }
                : null,
            async (id, c) => await GetRawJsonAsync($"{path}/{id}", c),
            body => GuidAt(body, "id"),
            ct
        );
    }

    /// <summary>The id-to-alias cache of a type kind, shared with the read projections (#163).</summary>
    /// <param name="kind">A document, media or member type.</param>
    /// <returns>The cache.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no alias cache.</exception>
    private Dictionary<Guid, string> AliasCache(EntityKind kind) =>
        kind switch
        {
            EntityKind.DocumentType => _documentTypeAliasById,
            EntityKind.MediaType => _mediaTypeAliasById,
            EntityKind.MemberType => _memberTypeAliasById,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a type kind."),
        };

    /// <summary>
    /// Fills <see cref="AliasCache"/> for the given types, reading only those not read yet, in
    /// batches. A type the server does not return keeps no alias.
    /// </summary>
    /// <param name="kind">A document, media or member type.</param>
    /// <param name="ids">The types.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the aliases are cached.</returns>
    /// <exception cref="ApiException">A batch request failed.</exception>
    private async Task ReadTypeAliasesAsync(
        EntityKind kind,
        IEnumerable<Guid> ids,
        CancellationToken ct
    )
    {
        var cache = AliasCache(kind);
        var unread = ids.Where(id => !cache.ContainsKey(id) && _aliasesRead.Add((kind, id)))
            .ToList();
        if (unread.Count == 0)
            return;
        foreach (var (id, body) in await ReadSchemaBodiesAsync(kind, unread, ct))
            if (body["alias"] is JsonValue value && value.TryGetValue<string>(out var alias))
                cache[id] = alias;
    }

    /// <summary>
    /// One page of a type list (#416): the page is cut from the cached tree first, then only its
    /// types' aliases are read, so <c>--take 5</c> costs one batch, not one read per type.
    /// </summary>
    /// <typeparam name="T">The type's response model.</typeparam>
    /// <param name="kind">A document, media or member type.</param>
    /// <param name="all">Every type, in tree order.</param>
    /// <param name="skip">Types to skip.</param>
    /// <param name="take">Types to return.</param>
    /// <param name="idOf">A type's id.</param>
    /// <param name="withAlias">Copies a type with its alias set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, with <c>Total</c> the number of types; an alias that could not be read is <c>""</c>.</returns>
    private async Task<PagedResponse<T>> TypePageAsync<T>(
        EntityKind kind,
        IReadOnlyList<T> all,
        int skip,
        int take,
        Func<T, Guid> idOf,
        Func<T, string, T> withAlias,
        CancellationToken ct
    )
    {
        var page = all.Skip(skip).Take(take).ToList();
        await ReadTypeAliasesAsync(kind, page.Select(idOf), ct);
        var aliases = AliasCache(kind);
        return new PagedResponse<T>
        {
            Total = all.Count,
            Items = [.. page.Select(t => withAlias(t, aliases.GetValueOrDefault(idOf(t), "")))],
        };
    }

    /// <summary>
    /// Resolves a document, media or member type reference (an alias or a name) to its id. The
    /// tree gives every type's id and name, and aliases are read a batch at a time until one
    /// matches, so the common case of one alias costs the tree walk and one batch. When no type
    /// has the alias, the name is matched instead (conventions 3.2, #358). The item search is
    /// deliberately not used: it indexes names, not aliases (ADR 0004).
    /// </summary>
    /// <param name="kind">A document, media or member type.</param>
    /// <param name="reference">The alias or name.</param>
    /// <param name="types">Every type of the kind, as (id, name), in tree order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The type id.</returns>
    /// <exception cref="ApiException">No type has the alias or name (404), or the name belongs to several (409).</exception>
    private async Task<Guid> FindTypeIdAsync(
        EntityKind kind,
        string reference,
        IReadOnlyList<(Guid Id, string Name)> types,
        CancellationToken ct
    )
    {
        var aliases = AliasCache(kind);
        foreach (var chunk in types.Chunk(TypeBatchSize))
        {
            await ReadTypeAliasesAsync(kind, chunk.Select(t => t.Id), ct);
            foreach (var (id, _) in chunk)
                if (
                    string.Equals(
                        aliases.GetValueOrDefault(id),
                        reference,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return id;
        }

        return ReferenceMatch.Pick(
            kind,
            reference,
            types.Select(t => new ReferenceCandidate(t.Id, aliases.GetValueOrDefault(t.Id), t.Name))
        );
    }

    /// <summary>Every document type, folders excluded and nested types included, walked once per client.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The types as the tree gives them (no alias).</returns>
    private async Task<List<DocumentTypeResponse>> DocumentTypeLeavesAsync(CancellationToken ct) =>
        _documentTypeLeaves ??= await CollectTreeLeavesAsync(FetchDocumentTypeTreeAsync, ct);

    /// <summary>Every media type, folders excluded and nested types included, walked once per client.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The types as the tree gives them (no alias).</returns>
    private async Task<List<MediaTypeResponse>> MediaTypeLeavesAsync(CancellationToken ct) =>
        _mediaTypeLeaves ??= await CollectTreeLeavesAsync(FetchMediaTypeTreeAsync, ct);

    /// <summary>Every member type, folders excluded and nested types included, walked once per client.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The types as the tree gives them (no alias).</returns>
    private async Task<List<MemberTypeResponse>> MemberTypeLeavesAsync(CancellationToken ct) =>
        _memberTypeLeaves ??= await CollectTreeLeavesAsync(FetchMemberTypeTreeAsync, ct);
}
