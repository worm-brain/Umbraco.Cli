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
    public static async Task<UmbracoResponse<SchemaSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        CancellationToken ct,
        bool includeFiles = true
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

        // #227: the rest of what a promotion needs before content can land. Languages come whole
        // from the list; the others are read per id like the types.
        var languages = await client.GetLanguagesRawAsync(ct);
        if (!languages.IsSuccess)
            return UmbracoResponse<SchemaSnapshot>.Failure(
                languages.StatusCode,
                languages.ErrorMessage!
            );

        var dictionary = await CollectDictionaryAsync(client, ct);
        if (!dictionary.IsSuccess)
            return Fail(dictionary);

        var memberGroups = await CollectAsync(
            () => client.GetMemberGroupIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.MemberGroup, id, ct)
        );
        if (!memberGroups.IsSuccess)
            return Fail(memberGroups);

        var userGroups = await CollectAsync(
            () => client.GetUserGroupIdsAsync(ct),
            id => client.GetSchemaRawAsync(EntityKind.UserGroup, id, ct)
        );
        if (!userGroups.IsSuccess)
            return Fail(userGroups);

        // #292: the files the templates render travel with them, so a promoted site does not
        // answer every page with a 500.
        var files = new Dictionary<string, List<JsonNode>>();
        if (includeFiles)
            foreach (var (tag, kind) in SchemaStaticFiles.Kinds)
            {
                var collected = await SchemaStaticFiles.CollectAsync(client, kind, ct);
                if (!collected.IsSuccess)
                    return Fail(collected);
                files[tag] = collected.Data!;
            }

        return UmbracoResponse<SchemaSnapshot>.Success(
            new SchemaSnapshot
            {
                DocumentTypes = docTypes.Data!,
                MediaTypes = mediaTypes.Data!,
                MemberTypes = memberTypes.Data!,
                DataTypes = dataTypes.Data!,
                Templates = templates.Data!,
                Languages = [.. languages.Data!],
                DictionaryItems = dictionary.Data!,
                MemberGroups = memberGroups.Data!,
                // Start nodes and per-document permissions name content on this instance only.
                UserGroups = [.. userGroups.Data!.Select(SchemaBodies.PortableUserGroup)],
                PartialViews = files.GetValueOrDefault(SchemaKinds.PartialView),
                Stylesheets = files.GetValueOrDefault(SchemaKinds.Stylesheet),
                Scripts = files.GetValueOrDefault(SchemaKinds.Script),
            }
        );
    }

    /// <summary>
    /// Reads every dictionary item with its parent added (the item read has none), so apply can
    /// create parents before children and move an item whose parent changed (#227).
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item's shaped body, or the first failure.</returns>
    private static async Task<UmbracoResponse<List<JsonNode>>> CollectDictionaryAsync(
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

        var bodies = new List<JsonNode>(entries.Data!.Count);
        foreach (var entry in entries.Data!)
        {
            var raw = await client.GetSchemaRawAsync(EntityKind.DictionaryItem, entry.Id, ct);
            if (!raw.IsSuccess)
                return UmbracoResponse<List<JsonNode>>.FailureFrom(raw);
            bodies.Add(SchemaBodies.DictionaryItem(raw.Data!, entry.ParentId));
        }
        return UmbracoResponse<List<JsonNode>>.Success(bodies);
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
            return UmbracoResponse<List<JsonNode>>.FailureFrom(ids);

        // Fetch the full body for each id, preserving enumeration order for stable output.
        var bodies = new List<JsonNode>(ids.Data!.Count);
        foreach (var id in ids.Data!)
        {
            var raw = await getRaw(id);
            if (!raw.IsSuccess)
                return UmbracoResponse<List<JsonNode>>.FailureFrom(raw);
            bodies.Add(raw.Data!);
        }

        return UmbracoResponse<List<JsonNode>>.Success(bodies);
    }

    /// <summary>Re-wraps a failed collection result as a failed snapshot result.</summary>
    /// <param name="failed">The failed intermediate result.</param>
    /// <returns>A failed <see cref="SchemaSnapshot"/> response carrying the same status/message.</returns>
    private static UmbracoResponse<SchemaSnapshot> Fail(UmbracoResponse<List<JsonNode>> failed) =>
        UmbracoResponse<SchemaSnapshot>.FailureFrom(failed);
}
