namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// What a content prune must leave alone (#225): documents of these types, and documents at or
/// under these roots. A whole-tree prune otherwise deletes everything created on the target
/// since the export, such as form submissions.
/// </summary>
/// <param name="DocumentTypeIds">Document types whose documents are never pruned.</param>
/// <param name="Roots">Documents that are never pruned, together with everything under them.</param>
public sealed record PruneExclusions(IReadOnlySet<Guid> DocumentTypeIds, IReadOnlySet<Guid> Roots)
{
    /// <summary>No exclusions.</summary>
    public static PruneExclusions None { get; } = new(new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>Whether there is nothing to exclude.</summary>
    public bool IsEmpty => DocumentTypeIds.Count == 0 && Roots.Count == 0;
}
