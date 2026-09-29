using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Assembles a <see cref="ContentSnapshot"/> from a live instance (issue #100 / ADR 0006). It
/// enumerates the content subtree in pre-order (capturing each document's parent, which the raw
/// body does not carry), then fetches each document's full verbatim body, a few at a time.
/// Enumeration + N per-id reads is O(documents), acceptable for a CI/agent tool.
///
/// Export <b>fails fast</b>: if the tree walk or any per-document read fails, the whole export
/// returns that failure rather than a partial snapshot - a partial content dump would silently
/// diff/apply as if documents had been deleted, exactly the footgun the pipeline must avoid.
/// </summary>
public static class ContentExporter
{
    /// <summary>
    /// Document reads in flight at once (#422): enough to hide most of a remote round trip, few
    /// enough not to queue much work on the server. 8 is what the data-type list hydration used
    /// before the batch reads (#418) replaced it.
    /// </summary>
    private const int BodyReadConcurrency = 8;

    /// <summary>
    /// Exports the content subtree beneath <paramref name="root"/> (or the whole content tree when
    /// null) of the instance behind <paramref name="client"/> into a snapshot.
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="root">The subtree root to export, or null for the whole content tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled snapshot, or the first failure encountered.</returns>
    public static async Task<UmbracoResponse<ContentSnapshot>> ExportAsync(
        IUmbracoManagementClient client,
        Guid? root,
        CancellationToken ct
    )
    {
        var tree = await client.GetDocumentTreeAsync(root, ct);
        if (!tree.IsSuccess)
            return UmbracoResponse<ContentSnapshot>.FailureFrom(tree);

        // The bodies are read a few at a time (#422); the results keep the pre-order tree order,
        // so parents still precede their children in the snapshot (which apply relies on for
        // create order), and a failed read fails the export with the failure earliest in that
        // order, as the one-at-a-time loop did.
        var documents = await ConcurrentReads.ReadAllAsync(
            tree.Data!,
            BodyReadConcurrency,
            async (node, c) =>
            {
                var raw = await client.GetDocumentRawAsync(node.Id, c);
                return raw.IsSuccess
                    ? UmbracoResponse<ContentNode>.Success(
                        new ContentNode
                        {
                            Id = node.Id,
                            Parent = node.Parent,
                            Body = raw.Data!,
                        }
                    )
                    : UmbracoResponse<ContentNode>.FailureFrom(raw);
            },
            ct
        );
        if (!documents.IsSuccess)
            return UmbracoResponse<ContentSnapshot>.FailureFrom(documents);
        return UmbracoResponse<ContentSnapshot>.Success(
            new ContentSnapshot { Root = root, Documents = [.. documents.Data!] }
        );
    }
}
