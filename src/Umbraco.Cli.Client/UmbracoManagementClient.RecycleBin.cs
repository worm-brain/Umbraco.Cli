using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// The recycle bins on <see cref="UmbracoManagementClient"/> (issues #67, #230, #364): trash a
/// document, restore it (to its original parent by default), list the content and media bins, and
/// empty the content bin.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>Moves a document to the recycle bin via <c>PUT document/{id}/move-to-recycle-bin</c> (issue #67).</summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> TrashContentAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .MoveToRecycleBin.PutAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Restores a document from the recycle bin via <c>PUT recycle-bin/document/{id}/restore</c>
    /// (issue #67).
    /// <para>
    /// By default the document goes back where it came from, as the backoffice does (#230):
    /// <c>GET recycle-bin/document/{id}/original-parent</c> answers the parent, or nothing when it
    /// was at the root. Restoring to the root by default put a <c>blogPost</c> where it is not
    /// allowed, and Umbraco's 400 ("not permitted, likely due to a permission/configuration
    /// mismatch") did not say why, so a rejected restore is reworded by
    /// <see cref="ExplainPlacementRefusal"/> to name where it was going, keeping Umbraco's body as
    /// the error's <c>details</c> (#395).
    /// </para>
    /// </summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="target">Where to restore to; null means <see cref="RestoreTarget.Original"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public async Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        RestoreTarget? target = null,
        CancellationToken ct = default
    )
    {
        target ??= RestoreTarget.Original;
        // Resolved inside the guarded call (the original parent is a server read) and kept here so
        // a refusal can say where the document was going.
        Guid? parentId = null;
        var response = await GuardedApiAsync(
            ct,
            async () =>
            {
                var bin = _api.Umbraco.Management.Api.V1.RecycleBin.Document[id];
                parentId = target switch
                {
                    RestoreTarget.UnderParent under => under.Id,
                    RestoreTarget.ContentRoot => (Guid?)null,
                    _ => (await bin.OriginalParent.GetAsync(cancellationToken: ct))?.Id,
                };
                await bin.Restore.PutAsync(
                    new Gen.MoveMediaRequestModel
                    {
                        Target = parentId is { } p ? new Gen.ReferenceByIdModel { Id = p } : null,
                    },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

        return ExplainPlacementRefusal(
            response,
            "restore",
            id,
            parentId,
            note: RestoreNote(target, parentId)
        );
    }

    /// <summary>
    /// Lists one level of the content recycle bin via <c>GET recycle-bin/document/root</c> or
    /// <c>/children</c> (#364), mapped like <see cref="GetContentAsync"/>'s tree rows (with the
    /// document-type alias filled) and flagged <c>isTrashed</c>.
    /// </summary>
    /// <param name="parentId">A trashed item whose children to list; null lists the bin's top level.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of trashed documents, or a mapped failure.</returns>
    public Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> GetContentRecycleBinAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var bin = _api.Umbraco.Management.Api.V1.RecycleBin.Document;
                var paged = parentId is { } pid
                    ? await bin.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = pid;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await bin.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<ContentItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = await WithDocumentTypeAliasesAsync(
                        (paged?.Items ?? []).Select(i => new ContentItemResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = (i.Variants ?? []).FirstOrDefault()?.Name ?? "",
                            DocumentType = MapDocumentTypeRef(i.DocumentType),
                            IsTrashed = true,
                            Parent = i.Parent?.Id is { } p
                                ? new ContentParentReference { Id = p }
                                : null,
                            // A trashed document is not live, whatever state its variants kept.
                            IsPublished = false,
                            CreateDate = i.CreateDate ?? default,
                        }),
                        ct
                    ),
                };
            }
        );

    /// <summary>
    /// Lists one level of the media recycle bin via <c>GET recycle-bin/media/root</c> or
    /// <c>/children</c> (#364), mapped like <see cref="GetMediaAsync"/>'s tree rows (with the
    /// media-type alias filled).
    /// </summary>
    /// <param name="parentId">A trashed item whose children to list; null lists the bin's top level.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of trashed media, or a mapped failure.</returns>
    public Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaRecycleBinAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var bin = _api.Umbraco.Management.Api.V1.RecycleBin.Media;
                var paged = parentId is { } pid
                    ? await bin.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = pid;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await bin.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<MediaItemResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = await WithMediaTypeAliasesAsync(
                        (paged?.Items ?? []).Select(i => new MediaItemResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = (i.Variants ?? []).FirstOrDefault()?.Name ?? "",
                            MediaType = i.MediaType?.Id is { } mt
                                ? new ContentTypeRef { Id = mt }
                                : null,
                            Parent = i.Parent?.Id is { } p
                                ? new ContentParentReference { Id = p }
                                : null,
                            CreateDate = i.CreateDate ?? default,
                        }),
                        ct
                    ),
                };
            }
        );

    /// <summary>Empties the content recycle bin via <c>DELETE recycle-bin/document</c> (issue #67). Irreversible.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> EmptyContentRecycleBinAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.RecycleBin.Document.DeleteAsync(
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );
}
