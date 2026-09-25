using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media sort</c> command (issue #88).</summary>
public static class MediaSortCommand
{
    /// <summary>
    /// Builds the <c>media sort</c> command: reorder a folder's children, either into an explicit
    /// order (<c>--order</c>) or by a field (<c>--by</c>, #232).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Reorder a folder's child media items, into the order given (first = top) or by a field.\n\n"
                + "Examples:\n"
                + "  umbraco media sort --parent 1a2b3c4d-... --order 3f7a...,9c4d...,2e6f...\n"
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
                    async (client, c) =>
                    {
                        var parent = parseResult.GetValue(parentOpt);
                        var order = await sort.OrderAsync(
                            parseResult,
                            (skip, take) => client.GetMediaAsync(parent, skip, take, c),
                            (child, key) => CandidateAsync(client, child, key, c),
                            c
                        );
                        // The data is the children in their new order (docs/conventions.md 6.2).
                        return order.IsSuccess
                            ? await client
                                .SortMediaAsync(parent, order.Data!, c)
                                .Then(ItemRefs.Of(order.Data!))
                            : UmbracoResponse<ItemRefs>.FailureFrom(order);
                    },
                    "Media children reordered.",
                    ct
                )
        );

        // A PUT under a verb the catalog's verb set does not know.
        return cmd.Mutating();
    }

    /// <summary>
    /// A child as a sort candidate. The listing carries the name and creation date; the update
    /// date needs a read of the child.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="child">The listed child.</param>
    /// <param name="key">What the children are ordered by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The candidate, or the read failure.</returns>
    private static async Task<UmbracoResponse<SortCandidate>> CandidateAsync(
        IUmbracoManagementClient client,
        MediaItemResponse child,
        SortKey key,
        CancellationToken ct
    )
    {
        if (key != SortKey.UpdateDate)
            return UmbracoResponse<SortCandidate>.Success(
                new SortCandidate(child.Id, child.Name, child.CreateDate, child.UpdateDate)
            );

        var full = await client.GetMediaByIdAsync(child.Id, ct);
        return full.IsSuccess
            ? UmbracoResponse<SortCandidate>.Success(
                new SortCandidate(child.Id, child.Name, child.CreateDate, full.Data!.UpdateDate)
            )
            : UmbracoResponse<SortCandidate>.FailureFrom(full);
    }
}
