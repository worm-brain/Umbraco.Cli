using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// One schema write: runs a create, update or delete for a diff entry against the client.
/// </summary>
/// <param name="client">The management client.</param>
/// <param name="change">The diff entry to act on.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>The write result.</returns>
public delegate Task<UmbracoResponse<Empty>> SchemaWrite(
    IUmbracoManagementClient client,
    SchemaEntityChange change,
    CancellationToken ct
);

/// <summary>
/// The write adapters the schema kind table (<see cref="SchemaKinds.All"/>) plugs into each kind
/// (#273). Most kinds share the generic raw create and full-replace update; the rest are here,
/// named, so the applier never switches on the kind.
/// </summary>
internal static class SchemaWrites
{
    /// <summary>The generic raw create for an id-keyed kind: the snapshot body is posted as it is.</summary>
    /// <param name="kind">The entity kind whose endpoint to post to.</param>
    /// <returns>The create adapter.</returns>
    public static SchemaWrite Create(EntityKind kind) =>
        (client, change, ct) => client.CreateSchemaRawAsync(kind, change.DesiredBody!, ct);

    /// <summary>
    /// The generic update for an id-keyed kind: a full replace (the snapshot body is the whole
    /// item) of the live entity, with the body's <c>id</c> rewritten to the live id so an
    /// alias-matched update whose snapshot id differs (see <see cref="SchemaEntityChange.IdMismatch"/>)
    /// targets the existing entity rather than trying to change its immutable id.
    /// </summary>
    /// <param name="kind">The entity kind whose endpoint to write to.</param>
    /// <returns>The update adapter.</returns>
    public static SchemaWrite Update(EntityKind kind) =>
        (client, change, ct) =>
            client.MergeSchemaItemAsync(
                kind,
                change.CurrentId!.Value,
                WithId(change.DesiredBody!, change.CurrentId!.Value),
                WriteMode.Replace,
                ct
            );

    /// <summary>A delete by the live id, through the kind's own delete call.</summary>
    /// <param name="delete">The client's delete call for the kind.</param>
    /// <returns>The delete adapter.</returns>
    public static SchemaWrite DeleteById(
        Func<IUmbracoManagementClient, Guid, CancellationToken, Task<UmbracoResponse<Empty>>> delete
    ) => (client, change, ct) => delete(client, change.CurrentId!.Value, ct);

    /// <summary>
    /// Runs one static-file operation (#292), addressed by path. A create or update sends the
    /// snapshot content byte for byte; a folder is created under its parent (the plan has created
    /// the parent already) and deleted by path.
    /// </summary>
    /// <param name="kind">The static-file kind.</param>
    /// <param name="operation">create, update or delete.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="InvalidOperationException">The operation is not create, update or delete.</exception>
    public static SchemaWrite File(StaticFileKind kind, string operation) =>
        async (client, change, ct) =>
        {
            var path = change.Identity;
            var folder = SchemaStaticFiles.IsFolder(change.DesiredBody ?? change.CurrentBody);
            switch (operation, folder)
            {
                case ("create", true):
                    return Done(
                        await client.CreateStaticFileFolderAsync(
                            kind,
                            SchemaStaticFiles.NameOf(path),
                            SchemaStaticFiles.ParentOf(path),
                            ct
                        )
                    );
                case ("create", false):
                    return Done(
                        await client.CreateStaticFileAsync(
                            kind,
                            new CreateStaticFileRequest
                            {
                                Name = SchemaStaticFiles.NameOf(path),
                                ParentPath = SchemaStaticFiles.ParentOf(path),
                                Content = SchemaStaticFiles.ContentOf(change.DesiredBody),
                            },
                            ct
                        )
                    );
                case ("update", _):
                    return await client.UpdateStaticFileAsync(
                        kind,
                        path,
                        new UpdateStaticFileRequest
                        {
                            Content = SchemaStaticFiles.ContentOf(change.DesiredBody),
                        },
                        ct
                    );
                case ("delete", true):
                    return await client.DeleteStaticFileFolderAsync(kind, path, ct);
                case ("delete", false):
                    return await client.DeleteStaticFileAsync(kind, path, ct);
                default:
                    throw new InvalidOperationException($"Unknown file operation {operation}.");
            }

            // The creates return the new item; the plan only needs to know it worked.
            static UmbracoResponse<Empty> Done<T>(UmbracoResponse<T> result) =>
                result.IsSuccess
                    ? UmbracoResponse<Empty>.Success(Empty.Value)
                    : UmbracoResponse<Empty>.FailureFrom(result);
        };

    /// <summary>
    /// Updates a dictionary item's name and translations, then moves it when its parent differs:
    /// the update model has no parent, so a re-parent is a separate call (#227).
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="change">The changed dictionary item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first failure, or an empty success.</returns>
    public static async Task<UmbracoResponse<Empty>> UpdateDictionaryItemAsync(
        IUmbracoManagementClient client,
        SchemaEntityChange change,
        CancellationToken ct
    )
    {
        var id = change.CurrentId!.Value;
        var update = await client.MergeSchemaItemAsync(
            EntityKind.DictionaryItem,
            id,
            WithId(SchemaBodies.WithoutParent(change.DesiredBody!), id),
            WriteMode.Replace,
            ct
        );
        if (!update.IsSuccess)
            return update;

        var parent = SchemaBodies.ParentOf(change.DesiredBody);
        return parent == SchemaBodies.ParentOf(change.CurrentBody)
            ? update
            : await client.MoveDictionaryItemAsync(id, parent, ct);
    }

    /// <summary>
    /// Updates a user group with the snapshot body plus the target's own start nodes and
    /// per-document permissions, which the snapshot leaves out (#227). The live group is read in
    /// full here, because the diff's live body has those parts removed too.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="change">The changed user group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first failure, or an empty success.</returns>
    public static async Task<UmbracoResponse<Empty>> UpdateUserGroupAsync(
        IUmbracoManagementClient client,
        SchemaEntityChange change,
        CancellationToken ct
    )
    {
        var id = change.CurrentId!.Value;
        var live = await client.GetSchemaRawAsync(EntityKind.UserGroup, id, ct);
        if (!live.IsSuccess)
            return UmbracoResponse<Empty>.FailureFrom(live);
        return await client.MergeSchemaItemAsync(
            EntityKind.UserGroup,
            id,
            WithId(SchemaBodies.WithLiveNodes(change.DesiredBody!, live.Data!), id),
            WriteMode.Replace,
            ct
        );
    }

    /// <summary>
    /// Returns a clone of <paramref name="body"/> with its top-level <c>id</c> set to
    /// <paramref name="id"/>, so an update targets the live entity's id. The original node is not
    /// mutated (it may be shared with the diff output).
    /// </summary>
    /// <param name="body">The snapshot body.</param>
    /// <param name="id">The live id to stamp.</param>
    /// <returns>A cloned body carrying the live id.</returns>
    private static JsonNode WithId(JsonNode body, Guid id)
    {
        var clone = body.DeepClone();
        clone["id"] = id.ToString();
        return clone;
    }
}
