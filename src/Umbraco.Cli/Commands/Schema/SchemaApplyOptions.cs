namespace Umbraco.Cli.Commands.Schema;

/// <summary>How a schema apply runs.</summary>
/// <param name="Prune">Also delete live entities the snapshot matched nothing to.</param>
/// <param name="DryRun">Compute the plan and write nothing.</param>
/// <param name="Force">Prune types even though content still uses them (#252).</param>
public sealed record SchemaApplyOptions(bool Prune, bool DryRun, bool Force = false);
