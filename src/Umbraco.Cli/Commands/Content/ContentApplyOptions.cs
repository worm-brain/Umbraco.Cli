namespace Umbraco.Cli.Commands.Content;

/// <summary>How a content apply runs.</summary>
/// <param name="Prune">Also delete live documents the snapshot matched nothing to.</param>
/// <param name="DryRun">Compute the plan and write nothing.</param>
public sealed record ContentApplyOptions(bool Prune, bool DryRun)
{
    /// <summary>Documents a prune must leave alone (#225). None by default.</summary>
    public PruneExclusions Exclude { get; init; } = PruneExclusions.None;

    /// <summary>
    /// Also publish and unpublish cultures so the target's publish state matches the snapshot's
    /// (#223). On by default: the snapshot promises the target will match it. <c>--no-state</c>
    /// turns it off, leaving new documents as drafts and live state untouched.
    /// </summary>
    public bool State { get; init; } = true;
}
