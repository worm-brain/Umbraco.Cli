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
    /// the diff of desired-vs-live, each change labelled with its document's name and document
    /// type alias (#293).
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
            return UmbracoResponse<ContentDiff>.FailureFrom(current);

        // The labels are best-effort: a failed language or type read leaves the row's name to the
        // first variant, or its documentType null, rather than failing the diff.
        var languages = await client.GetLanguagesAsync(ct);
        var defaultCulture = languages.IsSuccess
            ? languages.Data!.FirstOrDefault(l => l.IsDefault)?.IsoCode
            : null;
        var diff = ContentDiffEngine.Compare(desired, current.Data!, defaultCulture);
        return UmbracoResponse<ContentDiff>.Success(await WithTypeAliasesAsync(client, diff, ct));
    }

    /// <summary>
    /// Fills each change's document type alias with one lookup for the whole diff (#293), rather
    /// than one read per row.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff, whose changes carry document type ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The diff with aliases filled where they could be read.</returns>
    private static async Task<ContentDiff> WithTypeAliasesAsync(
        IUmbracoManagementClient client,
        ContentDiff diff,
        CancellationToken ct
    )
    {
        var ids = diff.Documents.Select(d => d.DocumentTypeId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
            return diff;

        var aliases = await client.GetDocumentTypeAliasesAsync(ids, ct);
        if (!aliases.IsSuccess)
            return diff;

        return diff with
        {
            Documents =
            [
                .. diff.Documents.Select(d =>
                    d.DocumentTypeId is { } id && aliases.Data!.TryGetValue(id, out var alias)
                        ? d with
                        {
                            DocumentType = alias,
                        }
                        : d
                ),
            ],
        };
    }
}
