using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content sort</c> command (issue #88).</summary>
public static class ContentSortCommand
{
    /// <summary>
    /// Builds the <c>content sort</c> command: reorder a parent's children, either into an explicit
    /// order (<c>--children</c>) or by a field (<c>--by</c>, #232).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Reorder a parent's child content items, into the order given (first = top) or by a field.\n\n"
                + "Examples:\n"
                + "  umbraco content sort --parent 1a2b3c4d-... --children 3f7a...,9c4d...,2e6f...\n"
                + "  umbraco content sort --parent 1a2b3c4d-... --by publishDate --desc   # newest first\n"
                + "  umbraco content sort --by name   # the content root, A to Z"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent ID whose children to reorder. Reorders the content root if omitted.",
        };
        cmd.Add(parentOpt);
        var sort = ChildSort.AddTo(
            cmd,
            "content",
            SortKey.Name,
            SortKey.CreateDate,
            SortKey.UpdateDate,
            SortKey.PublishDate
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content.sort",
                    async (client, c) =>
                    {
                        var parent = parseResult.GetValue(parentOpt);
                        IReadOnlyList<Guid> order = parseResult.GetValue(sort.Children) ?? [];
                        if (parseResult.GetValue(sort.By) is { } key)
                        {
                            var ordered = await OrderAsync(
                                client,
                                parent,
                                key,
                                parseResult.GetValue(sort.Descending),
                                c
                            );
                            if (!ordered.IsSuccess)
                                return UmbracoResponse<Empty>.FailureFrom(ordered);
                            order = ordered.Data!;
                        }
                        return await client.SortContentAsync(parent, order, c);
                    },
                    "Content children reordered.",
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Reads every child of <paramref name="parent"/> and orders them by <paramref name="key"/>.
    /// Names come from the listing; the dates need one read per child, because the tree does not
    /// carry them. A culture-variant document is dated by its first variant.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="parent">The parent, or null for the root.</param>
    /// <param name="key">What to order by.</param>
    /// <param name="descending">Whether to order descending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The child ids in the new order, or the first read failure.</returns>
    private static async Task<UmbracoResponse<IReadOnlyList<Guid>>> OrderAsync(
        IUmbracoManagementClient client,
        Guid? parent,
        SortKey key,
        bool descending,
        CancellationToken ct
    )
    {
        const int take = 100;
        var children = new List<ContentItemResponse>();
        while (true)
        {
            var page = await client.GetContentAsync(parent, children.Count, take, ct);
            if (!page.IsSuccess)
                return UmbracoResponse<IReadOnlyList<Guid>>.FailureFrom(page);
            var items = (page.Data?.Items ?? []).ToList();
            children.AddRange(items);
            if (items.Count < take || children.Count >= page.Data!.Total)
                break;
        }

        var candidates = new List<SortCandidate>();
        foreach (var child in children)
        {
            if (key == SortKey.Name)
            {
                candidates.Add(new SortCandidate(child.Id, child.Name));
                continue;
            }
            var full = await client.GetContentByIdAsync(child.Id, ct);
            if (!full.IsSuccess)
                return UmbracoResponse<IReadOnlyList<Guid>>.FailureFrom(full);
            var variant = full.Data?.Variants?.FirstOrDefault();
            candidates.Add(
                new SortCandidate(
                    child.Id,
                    child.Name,
                    variant?.CreateDate,
                    variant?.UpdateDate,
                    variant?.PublishDate
                )
            );
        }
        return UmbracoResponse<IReadOnlyList<Guid>>.Success(
            ChildSort.Order(candidates, key, descending)
        );
    }
}
