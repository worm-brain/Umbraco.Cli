using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Assembles a <see cref="SchemaSnapshot"/> from a live instance (issue #68 / ADR 0004 §4,
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
    /// Page size for enumerating each entity tree. Larger than the interactive default (20) to
    /// keep the number of list round-trips small on real installs.
    /// </summary>
    private const int PageSize = 100;

    /// <summary>
    /// Exports the full schema (document types, data types, templates) of the instance behind
    /// <paramref name="client"/> into a snapshot.
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled snapshot, or the first failure encountered.</returns>
    public static async Task<UmbracoResponse<SchemaSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        var docTypes = await CollectAsync(
            (skip, take) => client.GetDocumentTypesAsync(skip, take, ct),
            item => item.Id,
            id => client.GetDocumentTypeRawAsync(id, ct)
        );
        if (!docTypes.IsSuccess)
            return Fail(docTypes);

        var dataTypes = await CollectAsync(
            (skip, take) => client.GetDataTypesAsync(skip, take, ct),
            item => item.Id,
            id => client.GetDataTypeRawAsync(id, ct)
        );
        if (!dataTypes.IsSuccess)
            return Fail(dataTypes);

        var templates = await CollectAsync(
            (skip, take) => client.GetTemplatesAsync(skip, take, ct),
            item => item.Id,
            id => client.GetTemplateRawAsync(id, ct)
        );
        if (!templates.IsSuccess)
            return Fail(templates);

        return UmbracoResponse<SchemaSnapshot>.Success(
            new SchemaSnapshot
            {
                DocumentTypes = docTypes.Data!,
                DataTypes = dataTypes.Data!,
                Templates = templates.Data!,
            }
        );
    }

    /// <summary>
    /// Enumerates every entity of one kind (paging the list) and reads each one's raw body.
    /// </summary>
    /// <typeparam name="TItem">The list item type (carries the id).</typeparam>
    /// <param name="listPage">Reads one page of list items for a given skip/take.</param>
    /// <param name="idOf">Extracts the entity id from a list item.</param>
    /// <param name="getRaw">Reads one entity's verbatim body by id.</param>
    /// <returns>Every entity's raw body, or the first failure.</returns>
    private static async Task<UmbracoResponse<List<JsonNode>>> CollectAsync<TItem>(
        Func<int, int, Task<UmbracoResponse<PagedResponse<TItem>>>> listPage,
        Func<TItem, Guid> idOf,
        Func<Guid, Task<UmbracoResponse<JsonNode>>> getRaw
    )
    {
        // 1) Enumerate all ids by paging the tree root until we have every item.
        var ids = new List<Guid>();
        var skip = 0;
        while (true)
        {
            var page = await listPage(skip, PageSize);
            if (!page.IsSuccess)
                return UmbracoResponse<List<JsonNode>>.Failure(page.StatusCode, page.ErrorMessage!);

            var items = page.Data?.Items?.ToList() ?? [];
            ids.AddRange(items.Select(idOf));

            skip += items.Count;
            // Stop when this page was empty (defensive against a wrong Total) or we've reached
            // the reported total. Both guard against an infinite loop.
            if (items.Count == 0 || skip >= (page.Data?.Total ?? ids.Count))
                break;
        }

        // 2) Fetch the full body for each id, preserving enumeration order for stable output.
        var bodies = new List<JsonNode>(ids.Count);
        foreach (var id in ids)
        {
            var raw = await getRaw(id);
            if (!raw.IsSuccess)
                return UmbracoResponse<List<JsonNode>>.Failure(raw.StatusCode, raw.ErrorMessage!);
            bodies.Add(raw.Data!);
        }

        return UmbracoResponse<List<JsonNode>>.Success(bodies);
    }

    /// <summary>Re-wraps a failed collection result as a failed snapshot result.</summary>
    /// <param name="failed">The failed intermediate result.</param>
    /// <returns>A failed <see cref="SchemaSnapshot"/> response carrying the same status/message.</returns>
    private static UmbracoResponse<SchemaSnapshot> Fail(UmbracoResponse<List<JsonNode>> failed) =>
        UmbracoResponse<SchemaSnapshot>.Failure(failed.StatusCode, failed.ErrorMessage!);
}
