using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Dictionary;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Assembles a <see cref="SchemaSnapshot"/> from a live instance (issue #68 / ADR 0005 §4,
/// export). For each entity kind it enumerates every id through the existing paged tree-root
/// list method (there is no flat collection endpoint — #39), then fetches the full verbatim
/// body per id via <see cref="ISchemaClient"/>. Enumeration + N per-id reads is O(entities),
/// acceptable for a CI/agent tool.
///
/// Export **fails fast**: if any list page or per-entity read fails, the whole export returns
/// that failure rather than a partial snapshot — a partial schema dump would silently diff/
/// apply as if entities had been deleted, which is exactly the footgun the pipeline must avoid.
/// </summary>
public static class SchemaExporter
{
    /// <summary>
    /// Dictionary item reads in flight at once (#422): 8, as for the content export's document
    /// reads.
    /// </summary>
    private const int DictionaryReadConcurrency = 8;

    /// <summary>
    /// Exports the full schema (document types, media types, member types, data types,
    /// templates, languages, dictionary items, member groups and user groups) of the instance
    /// behind <paramref name="client"/> into a snapshot, with its partial views, stylesheets and
    /// scripts unless <paramref name="includeFiles"/> is false (#292).
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="includeFiles">
    /// Whether to read the static files. False leaves the three sections out (null), which marks
    /// the snapshot as not managing files: <c>schema export --no-files</c>, and the live side of a
    /// diff against a snapshot that does not manage them (no point reading every file).
    /// </param>
    /// <returns>The assembled snapshot, or the first failure encountered.</returns>
    public static Task<UmbracoResponse<SchemaSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        CancellationToken ct,
        bool includeFiles = true
    ) =>
        ExportAsync(
            client,
            SchemaKinds.All.Where(k => includeFiles || k.File is null).ToList(),
            ct
        );

    /// <summary>
    /// Exports only <paramref name="kinds"/>; every other section is absent. The live side of a
    /// diff reads just the kinds the snapshot manages (#198), so a partial snapshot does not read
    /// the whole instance.
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="kinds">The kinds to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled snapshot, or the first failure encountered.</returns>
    public static async Task<UmbracoResponse<SchemaSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        IReadOnlyCollection<SchemaKindSpec> kinds,
        CancellationToken ct
    )
    {
        // One read per kind in the kind table (#273). A kind left out is absent.
        var snapshot = new SchemaSnapshot();
        foreach (var kind in SchemaKinds.All)
        {
            if (!kinds.Contains(kind))
            {
                kind.SetSection(snapshot, null);
                continue;
            }
            var entries = await kind.Export(client, ct);
            if (!entries.IsSuccess)
                return UmbracoResponse<SchemaSnapshot>.FailureFrom(entries);
            kind.SetSection(snapshot, entries.Data!);
        }

        // The dictionary's values only mean something next to the format they are in (#442), so
        // a snapshot that carries them says which. One manifest read, and only then.
        if (kinds.Any(k => k.Tag == SchemaKinds.DictionaryItem))
            snapshot.DictionaryValueFormat = await DictionaryValueFormat.ReadAsync(client, ct);
        return UmbracoResponse<SchemaSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Reads every dictionary item with its parent added (the item read has none), so apply can
    /// create parents before children and move an item whose parent changed (#227).
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item's shaped body, or the first failure.</returns>
    internal static async Task<UmbracoResponse<List<JsonNode>>> CollectDictionaryAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        var entries = await client.GetDictionaryEntriesAsync(ct);
        if (!entries.IsSuccess)
            return UmbracoResponse<List<JsonNode>>.Failure(
                entries.StatusCode,
                entries.ErrorMessage!
            );

        // Umbraco has no batch read for dictionary items, so they are read by id, a few at a time
        // (#422), in the order the entries are listed.
        var bodies = await ConcurrentReads.ReadAllAsync(
            entries.Data!,
            DictionaryReadConcurrency,
            async (entry, c) =>
            {
                var raw = await client.GetSchemaRawAsync(EntityKind.DictionaryItem, entry.Id, c);
                return raw.IsSuccess
                    ? UmbracoResponse<JsonNode>.Success(
                        SchemaBodies.DictionaryItem(raw.Data!, entry.ParentId)
                    )
                    : raw;
            },
            ct
        );
        return bodies.IsSuccess
            ? UmbracoResponse<List<JsonNode>>.Success([.. bodies.Data!])
            : UmbracoResponse<List<JsonNode>>.FailureFrom(bodies);
    }

    /// <summary>
    /// Enumerates every entity id of one kind (the client walks the tree, skipping folders and
    /// recursing nested entities) and reads their raw bodies.
    /// </summary>
    /// <param name="listIds">Enumerates every entity id of the kind.</param>
    /// <param name="getRaw">
    /// Reads the bodies of the given ids, in their order, or fails on the first that cannot be
    /// read (<see cref="ISchemaClient.GetSchemaRawManyAsync"/>).
    /// </param>
    /// <returns>Every entity's raw body in enumeration order, or the first failure.</returns>
    internal static async Task<UmbracoResponse<List<JsonNode>>> CollectAsync(
        Func<Task<UmbracoResponse<IReadOnlyList<Guid>>>> listIds,
        Func<IReadOnlyList<Guid>, Task<UmbracoResponse<IReadOnlyList<JsonNode>>>> getRaw
    )
    {
        var ids = await listIds();
        if (!ids.IsSuccess)
            return UmbracoResponse<List<JsonNode>>.FailureFrom(ids);

        var bodies = await getRaw(ids.Data!);
        return bodies.IsSuccess
            ? UmbracoResponse<List<JsonNode>>.Success([.. bodies.Data!])
            : UmbracoResponse<List<JsonNode>>.FailureFrom(bodies);
    }
}
