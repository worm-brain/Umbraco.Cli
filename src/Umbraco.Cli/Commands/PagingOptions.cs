using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The shared <c>--skip</c> / <c>--take</c> / <c>--all</c> paging options every paged command uses
/// (docs/conventions.md 4.1, 5.3). One default page size for all of them: output carries
/// <c>meta.total</c> / <c>meta.hasMore</c>, so a larger first page saves round trips without
/// hiding anything.
/// </summary>
public static class PagingOptions
{
    /// <summary>The default <c>--take</c> for every paged command.</summary>
    public const int DefaultTake = 100;

    /// <summary>
    /// The most items <c>--all</c> will collect. Reaching it fails the command rather than
    /// returning a quietly truncated list (#196), which is the bug <c>--all</c> exists to end.
    /// Matches the bound on the tree walks.
    /// </summary>
    public const int MaxAllItems = 10_000;

    // The --all option of each paged command, keyed by the command so the executor can find it
    // from a ParseResult without every call site threading a third handle through.
    private static readonly ConditionalWeakTable<Command, Option<bool>> AllOptions = new();

    /// <summary>
    /// Adds a <c>--skip</c>/<c>--take</c> pair and an <c>--all</c> flag to a command, with the rule
    /// that <c>--all</c> cannot be combined with an explicit <c>--skip</c> or <c>--take</c>.
    /// </summary>
    /// <param name="cmd">The command to add the options to.</param>
    /// <returns>The skip and take option handles; <c>--all</c> is read by the executor.</returns>
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
        var all = new Option<bool>("--all")
        {
            Description =
                $"Read every page and return the whole list (at most {MaxAllItems}; more is an error). "
                + "Not with --skip or --take.",
        };
        cmd.Add(skip);
        cmd.Add(take);
        cmd.Add(all);
        AllOptions.AddOrUpdate(cmd, all);

        // Two inputs that conflict are an error, never a silent precedence (conventions 4.5):
        // --all with a page window would have to ignore one of them. Implicit is true when the
        // value came from the default, so only a skip/take the caller typed conflicts.
        cmd.Validators.Add(result =>
        {
            if (
                result.GetValue(all)
                && (
                    result.GetResult(skip) is { Implicit: false }
                    || result.GetResult(take) is { Implicit: false }
                )
            )
                result.AddError(
                    "--all reads every page, so it cannot be combined with --skip or --take."
                );
        });
        return (skip, take);
    }

    /// <summary>Whether the parsed command is a paged command run with <c>--all</c>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>True when <c>--all</c> was given.</returns>
    public static bool AllRequested(ParseResult parseResult) =>
        AllOptions.TryGetValue(parseResult.CommandResult.Command, out var all)
        && parseResult.GetValue(all);
}
