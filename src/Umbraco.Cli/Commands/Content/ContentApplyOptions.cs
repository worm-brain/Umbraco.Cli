namespace Umbraco.Cli.Commands.Content;

/// <summary>How a content apply runs.</summary>
/// <param name="Prune">Also delete live documents the snapshot matched nothing to.</param>
/// <param name="DryRun">Compute the plan and write nothing.</param>
public sealed record ContentApplyOptions(bool Prune, bool DryRun)
{
    /// <summary>Documents a prune must leave alone (#225). None by default.</summary>
    public PruneExclusions Exclude { get; init; } = PruneExclusions.None;
}
