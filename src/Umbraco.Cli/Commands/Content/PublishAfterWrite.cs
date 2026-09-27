using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The <c>--publish</c> step shared by <c>content version rollback</c> and <c>content restore</c>
/// (#233). Both writes leave the live site untouched - a rollback only changes the draft, and a
/// restored item comes back unpublished - so publishing afterwards is a separate call, made only
/// when the write succeeded.
/// </summary>
public static class PublishAfterWrite
{
    /// <summary>
    /// Awaits <paramref name="write"/> and, when it succeeded, publishes <paramref name="documentId"/>.
    /// A failed publish is reported as such, saying the write itself did land, so a caller does not
    /// retry the write.
    /// </summary>
    /// <param name="write">The rollback or restore.</param>
    /// <param name="client">The client to publish with.</param>
    /// <param name="documentId">The document to publish.</param>
    /// <param name="cultures">Cultures to publish; null publishes every culture the document has.</param>
    /// <param name="done">What the write did, for the partial-failure message (e.g. "Rolled back").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write's failure, the publish's failure, or a success.</returns>
    public static async Task<UmbracoResponse<Empty>> ThenPublishAsync(
        this Task<UmbracoResponse<Empty>> write,
        IUmbracoManagementClient client,
        Guid documentId,
        IEnumerable<string>? cultures,
        string done,
        CancellationToken ct
    )
    {
        var result = await write;
        if (!result.IsSuccess)
            return result;

        var published = await client.PublishContentAsync(documentId, cultures, ct: ct);
        return published.IsSuccess
            ? published
            : UmbracoResponse<Empty>.Failure(
                published.StatusCode,
                $"{done}, but publishing failed: {published.ErrorMessage} "
                    + $"Run 'umbraco content publish {documentId}' to retry the publish.",
                published.Category
            );
    }

    /// <summary>
    /// The document a version belongs to, read from <c>GET document-version/{id}</c>'s
    /// <c>document.id</c>. A rollback names a version, so this is how <c>--publish</c> knows what
    /// to publish.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="versionId">The version id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document id, or the read's failure (a 404 when the version does not exist).</returns>
    public static async Task<UmbracoResponse<Guid>> DocumentOfVersionAsync(
        IUmbracoManagementClient client,
        Guid versionId,
        CancellationToken ct
    )
    {
        var version = await client.GetDocumentVersionAsync(versionId, ct);
        if (!version.IsSuccess)
            return UmbracoResponse<Guid>.FailureFrom(version);

        return
            version.Data?["document"]?["id"] is JsonValue id
            && Guid.TryParse(id.ToString(), out var documentId)
            ? UmbracoResponse<Guid>.Success(documentId)
            : UmbracoResponse<Guid>.Failure(
                502,
                $"Version {versionId} did not say which document it belongs to, so it cannot be published."
            );
    }
}
