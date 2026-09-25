using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

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
    /// Exports the full schema (document types, media types, member types, data types,
    /// templates) of the instance behind <paramref name="client"/> into a snapshot.
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
            () => client.GetDocumentTypeIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.DocumentType, id, ct)
        );
        if (!docTypes.IsSuccess)
            return Fail(docTypes);

        var dataTypes = await CollectAsync(
            () => client.GetDataTypeIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.DataType, id, ct)
        );
        if (!dataTypes.IsSuccess)
            return Fail(dataTypes);

        var templates = await CollectAsync(
            () => client.GetTemplateIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.Template, id, ct)
        );
        if (!templates.IsSuccess)
            return Fail(templates);

        // #186: media types and member types were the one part of the schema the snapshot could
        // not carry, so authoring either one meant a direct Management API call.
        var mediaTypes = await CollectAsync(
            () => client.GetMediaTypeIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.MediaType, id, ct)
        );
        if (!mediaTypes.IsSuccess)
            return Fail(mediaTypes);

        var memberTypes = await CollectAsync(
            () => client.GetMemberTypeIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.MemberType, id, ct)
        );
        if (!memberTypes.IsSuccess)
            return Fail(memberTypes);

        return UmbracoResponse<SchemaSnapshot>.Success(
            new SchemaSnapshot
            {
                DocumentTypes = docTypes.Data!,
                MediaTypes = mediaTypes.Data!,
                MemberTypes = memberTypes.Data!,
                DataTypes = dataTypes.Data!,
                Templates = templates.Data!,
            }
        );
    }

    /// <summary>
    /// Enumerates every entity id of one kind (the client walks the tree, skipping folders and
    /// recursing nested entities) and reads each one's raw body.
    /// </summary>
    /// <param name="listIds">Enumerates every entity id of the kind.</param>
    /// <param name="getRaw">Reads one entity's verbatim body by id.</param>
    /// <returns>Every entity's raw body, or the first failure.</returns>
    private static async Task<UmbracoResponse<List<JsonNode>>> CollectAsync(
        Func<Task<UmbracoResponse<IReadOnlyList<Guid>>>> listIds,
        Func<Guid, Task<UmbracoResponse<JsonNode>>> getRaw
    )
    {
        var ids = await listIds();
        if (!ids.IsSuccess)
            return UmbracoResponse<List<JsonNode>>.Failure(ids.StatusCode, ids.ErrorMessage!);

        // Fetch the full body for each id, preserving enumeration order for stable output.
        var bodies = new List<JsonNode>(ids.Data!.Count);
        foreach (var id in ids.Data!)
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
