using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Assembles a <see cref="ContentSnapshot"/> from a live instance (issue #100 / ADR 0006). It
/// enumerates the content subtree in pre-order (capturing each document's parent, which the raw
/// body does not carry), then fetches each document's full verbatim body. Enumeration + N per-id
/// reads is O(documents), acceptable for a CI/agent tool.
///
/// Export <b>fails fast</b>: if the tree walk or any per-document read fails, the whole export
/// returns that failure rather than a partial snapshot - a partial content dump would silently
/// diff/apply as if documents had been deleted, exactly the footgun the pipeline must avoid.
/// </summary>
public static class ContentExporter
{
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
            return UmbracoResponse<ContentSnapshot>.Failure(tree.StatusCode, tree.ErrorMessage!);

        // Fetch each document's full body, preserving the pre-order tree order so parents always
        // precede their children in the snapshot (which is what apply relies on for create order).
        var documents = new List<ContentNode>(tree.Data!.Count);
        foreach (var node in tree.Data!)
        {
            var raw = await client.GetDocumentRawAsync(node.Id, ct);
            if (!raw.IsSuccess)
                return UmbracoResponse<ContentSnapshot>.Failure(raw.StatusCode, raw.ErrorMessage!);

            documents.Add(
                new ContentNode
                {
                    Id = node.Id,
                    Parent = node.Parent,
                    Body = raw.Data!,
                }
            );
        }

        return UmbracoResponse<ContentSnapshot>.Success(
            new ContentSnapshot { Root = root, Documents = documents }
        );
    }
}
