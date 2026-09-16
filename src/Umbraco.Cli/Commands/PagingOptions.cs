using System.CommandLine;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The shared <c>--skip</c> / <c>--take</c> paging option pair used by list and query verbs. Each
/// caller keeps its own <paramref name="defaultTake"/> so behaviour is unchanged; centralising the
/// declaration removes the per-verb boilerplate.
/// </summary>
public static class PagingOptions
{
    /// <summary>Adds a <c>--skip</c>/<c>--take</c> pair to a command and returns the option handles.</summary>
    /// <param name="cmd">The command to add the options to.</param>
    /// <param name="defaultTake">The default value for <c>--take</c> (preserve each verb's existing default).</param>
    /// <returns>The skip and take option handles.</returns>
    public static (Option<int> Skip, Option<int> Take) Add(Command cmd, int defaultTake)
    {
        var skip = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var take = new Option<int>("--take") { DefaultValueFactory = _ => defaultTake };
        cmd.Add(skip);
        cmd.Add(take);
        return (skip, take);
    }
}
