using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// The content recycle bin on <see cref="UmbracoManagementClient"/> (issues #67, #230): trash a
/// document, restore it (to its original parent by default), and empty the bin.
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
    /// mismatch") did not say why, so a rejected restore is reworded to name where it was going.
    /// </para>
    /// </summary>
    /// <param name="id">The trashed document id.</param>
    /// <param name="target">Where to restore to; null means <see cref="RestoreTarget.Original"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> RestoreContentAsync(
        Guid id,
        RestoreTarget? target = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var bin = _api.Umbraco.Management.Api.V1.RecycleBin.Document[id];
                target ??= RestoreTarget.Original;
                var parentId = target switch
                {
                    RestoreTarget.UnderParent under => under.Id,
                    RestoreTarget.ContentRoot => (Guid?)null,
                    _ => (await bin.OriginalParent.GetAsync(cancellationToken: ct))?.Id,
                };

                try
                {
                    await bin.Restore.PutAsync(
                        new Gen.MoveMediaRequestModel
                        {
                            Target = parentId is { } p
                                ? new Gen.ReferenceByIdModel { Id = p }
                                : null,
                        },
                        cancellationToken: ct
                    );
                }
                catch (ApiException ex) when (Describe(ex).Status == 400)
                {
                    var original = target is RestoreTarget.OriginalParent;
                    var where = parentId is { } p
                        ? $"under {p}" + (original ? " (its original parent)" : "")
                        : "at the content root" + (original ? " (where it was)" : "");
                    throw BadRequest(
                        $"Umbraco would not restore {id} {where}: {Describe(ex).Message.TrimEnd('.')}. "
                            + "Its document type may not be allowed there; pass --parent <id> to restore it somewhere else."
                    );
                }
                return Empty.Value;
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
