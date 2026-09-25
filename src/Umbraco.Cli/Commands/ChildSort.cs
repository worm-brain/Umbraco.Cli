using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>What <c>content sort --by</c> / <c>media sort --by</c> orders children by (#232).</summary>
public enum SortKey
{
    /// <summary>The item name, ignoring case.</summary>
    Name,

    /// <summary>When the item was created.</summary>
    CreateDate,

    /// <summary>When the item was last saved.</summary>
    UpdateDate,

    /// <summary>When the item was published (content only).</summary>
    PublishDate,
}

/// <summary>One child as <see cref="ChildSort.Order"/> sees it.</summary>
/// <param name="Id">The child id.</param>
/// <param name="Name">The child name.</param>
/// <param name="CreateDate">When it was created, if known.</param>
/// <param name="UpdateDate">When it was last saved, if known.</param>
/// <param name="PublishDate">When it was published, if known (never for media).</param>
public sealed record SortCandidate(
    Guid Id,
    string Name,
    DateTimeOffset? CreateDate = null,
    DateTimeOffset? UpdateDate = null,
    DateTimeOffset? PublishDate = null
);

/// <summary>
/// <c>sort --by</c> (#232): the common case - order a blog's posts by publish date, newest first -
/// used to need a get-per-child script to build the <c>--children</c> list by hand.
/// </summary>
public static class ChildSort
{
    /// <summary>The <c>--children</c> / <c>--by</c> / <c>--desc</c> options shared by both sort commands.</summary>
    /// <param name="Children">The explicit order.</param>
    /// <param name="By">The key to order by instead.</param>
    /// <param name="Descending">Whether <c>--by</c> orders descending.</param>
    public sealed record Options(
        Option<Guid[]> Children,
        Option<SortKey?> By,
        Option<bool> Descending
    );

    /// <summary>
    /// Adds <c>--children</c>, <c>--by</c> and <c>--desc</c>, and requires exactly one of
    /// <c>--children</c> and <c>--by</c>.
    /// </summary>
    /// <param name="cmd">The sort command.</param>
    /// <param name="noun">What the children are, for the help text (e.g. <c>content</c>).</param>
    /// <param name="keys">The keys this noun supports.</param>
    /// <returns>The options.</returns>
    public static Options AddTo(Command cmd, string noun, params SortKey[] keys)
    {
        var children = ListOption.Guids(
            "--children",
            $"Child {noun} ids in the desired order (first gets sort order 0)."
        );
        var by = new Option<SortKey?>("--by")
        {
            Description =
                $"Order every child by a field instead of listing them: {string.Join(", ", keys.Select(Camel))}.",
            CustomParser = result =>
            {
                var text = result.Tokens.Single().Value;
                SortKey? match = keys.Where(k =>
                        string.Equals(Camel(k), text, StringComparison.OrdinalIgnoreCase)
                    )
                    .Cast<SortKey?>()
                    .FirstOrDefault();
                if (match is null)
                    result.AddError(
                        $"--by expects one of {string.Join(", ", keys.Select(Camel))}. Not understood: {text}."
                    );
                return match;
            },
        };
        var desc = new Option<bool>("--desc")
        {
            Description = "With --by, order descending (newest first).",
        };
        cmd.Add(children);
        cmd.Add(by);
        cmd.Add(desc);
        ListOption.ValidateParsed(
            cmd,
            result =>
            {
                var hasChildren = result.GetValue(children) is { Length: > 0 };
                var hasBy = result.GetValue(by) is not null;
                if (hasChildren == hasBy)
                    result.AddError(
                        "Give either --children (an explicit order) or --by (a field to order by)."
                    );
                else if (result.GetValue(desc) && !hasBy)
                    result.AddError("--desc only applies with --by.");
            }
        );
        return new Options(children, by, desc);
    }

    /// <summary>
    /// Orders <paramref name="children"/> by <paramref name="key"/>. The sort is stable, so ties
    /// keep their current order, and an item without the date sorts last either way.
    /// </summary>
    /// <param name="children">The children in their current order.</param>
    /// <param name="key">What to order by.</param>
    /// <param name="descending">Whether to order descending.</param>
    /// <returns>The child ids in the new order.</returns>
    public static IReadOnlyList<Guid> Order(
        IEnumerable<SortCandidate> children,
        SortKey key,
        bool descending
    )
    {
        var list = children.ToList();
        if (key == SortKey.Name)
        {
            var byName = descending
                ? list.OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase)
                : list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
            return [.. byName.Select(c => c.Id)];
        }

        DateTimeOffset? Date(SortCandidate c) =>
            key switch
            {
                SortKey.CreateDate => c.CreateDate,
                SortKey.UpdateDate => c.UpdateDate,
                _ => c.PublishDate,
            };
        var dated = list.Where(c => Date(c) is not null);
        var ordered = descending
            ? dated.OrderByDescending(c => Date(c))
            : dated.OrderBy(c => Date(c));
        return [.. ordered.Concat(list.Where(c => Date(c) is null)).Select(c => c.Id)];
    }

    /// <summary>The camelCase spelling the option takes, e.g. <c>publishDate</c>.</summary>
    private static string Camel(SortKey key) =>
        char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..];
}
