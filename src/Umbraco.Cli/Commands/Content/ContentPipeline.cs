using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The shared prelude for <c>content diff</c> and <c>content apply</c> (issue #100): load a
/// snapshot file, export the live content at the <b>same scope</b> the snapshot was taken (its
/// recorded <see cref="ContentSnapshot.Root"/>), and compute the diff. Exporting at the snapshot's
/// own root is what keeps a subtree snapshot from diffing every out-of-scope live document as
/// "removed" - the safety property that makes <c>apply --prune</c> sound.
/// </summary>
public static class ContentPipeline
{
    /// <summary>
    /// Loads the snapshot at <paramref name="snapshotPath"/> (or stdin for <c>-</c>), exports the
    /// live content behind <paramref name="client"/> at the snapshot's recorded root, and returns
    /// the diff of desired-vs-live.
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="snapshotPath">The snapshot file path, or <c>-</c> for stdin.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed diff, or the export failure. Snapshot load errors surface as exceptions the executor maps to a clean error.</returns>
    public static async Task<UmbracoResponse<ContentDiff>> DiffAgainstLiveAsync(
        IUmbracoManagementClient client,
        string snapshotPath,
        CancellationToken ct
    )
    {
        var desired = await ContentFile.LoadAsync(snapshotPath, ct);
        var current = await ContentExporter.ExportAsync(client, desired.Root, ct);
        if (!current.IsSuccess)
            return UmbracoResponse<ContentDiff>.Failure(current.StatusCode, current.ErrorMessage!);

        return UmbracoResponse<ContentDiff>.Success(
            ContentDiffEngine.Compare(desired, current.Data!)
        );
    }
}
