using System.CommandLine;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The shared <c>--skip</c> / <c>--take</c> paging pair every paged command uses
/// (docs/conventions.md 5.3). One default page size for all of them: output carries
/// <c>meta.total</c> / <c>meta.hasMore</c>, so a larger first page saves round trips without
/// hiding anything.
/// </summary>
public static class PagingOptions
{
    /// <summary>The default <c>--take</c> for every paged command.</summary>
    public const int DefaultTake = 100;

    /// <summary>Adds a <c>--skip</c>/<c>--take</c> pair to a command and returns the option handles.</summary>
    /// <param name="cmd">The command to add the options to.</param>
    /// <returns>The skip and take option handles.</returns>
    public static (Option<int> Skip, Option<int> Take) Add(Command cmd)
    {
        var skip = new Option<int>("--skip")
        {
            DefaultValueFactory = _ => 0,
            Description = "Number of items to skip, for paging (default 0).",
        };
        var take = new Option<int>("--take")
        {
            DefaultValueFactory = _ => DefaultTake,
            Description =
                $"Maximum number of items to return (default {DefaultTake}). meta.hasMore says whether there are more.",
        };
        cmd.Add(skip);
        cmd.Add(take);
        return (skip, take);
    }
}
