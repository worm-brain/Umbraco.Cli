using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media sort</c> command (issue #88).</summary>
public static class MediaSortCommand
{
    /// <summary>
    /// Builds the <c>media sort</c> command: reorder a folder's children, either into an explicit
    /// order (<c>--children</c>) or by a field (<c>--by</c>, #232).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Reorder a folder's child media items, into the order given (first = top) or by a field.\n\n"
                + "Examples:\n"
                + "  umbraco media sort --parent 1a2b3c4d-... --children 3f7a...,9c4d...,2e6f...\n"
                + "  umbraco media sort --parent 1a2b3c4d-... --by name\n"
                + "  umbraco media sort --by createDate --desc   # the media root, newest first"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent folder ID whose children to reorder. Reorders the media root if omitted.",
        };
        cmd.Add(parentOpt);
        var sort = ChildSort.AddTo(
            cmd,
            "media",
            SortKey.Name,
            SortKey.CreateDate,
            SortKey.UpdateDate
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media.sort",
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
                        return await client.SortMediaAsync(parent, order, c);
                    },
                    "Media children reordered.",
                    ct
                )
        );

        return cmd;
    }

    /// <summary>
    /// Reads every child of <paramref name="parent"/> and orders them by <paramref name="key"/>.
    /// The listing carries names and creation dates; the update date needs one read per child.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="parent">The parent folder, or null for the root.</param>
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
        var children = new List<MediaItemResponse>();
        while (true)
        {
            var page = await client.GetMediaAsync(parent, children.Count, take, ct);
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
            var updated = child.UpdateDate;
            if (key == SortKey.UpdateDate)
            {
                var full = await client.GetMediaByIdAsync(child.Id, ct);
                if (!full.IsSuccess)
                    return UmbracoResponse<IReadOnlyList<Guid>>.FailureFrom(full);
                updated = full.Data!.UpdateDate;
            }
            candidates.Add(new SortCandidate(child.Id, child.Name, child.CreateDate, updated));
        }
        return UmbracoResponse<IReadOnlyList<Guid>>.Success(
            ChildSort.Order(candidates, key, descending)
        );
    }
}
