using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content sort</c> command (issue #88).</summary>
public static class ContentSortCommand
{
    /// <summary>
    /// Builds the <c>content sort</c> command: reorder a parent's children, either into an explicit
    /// order (<c>--order</c>) or by a field (<c>--by</c>, #232).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "sort",
            "Sort a parent's child content items, in the order given or by a field.\n\n"
                + "With --order the first id goes to the top."
        ).WithExamples(
            "umbraco content sort --parent 1a2b3c4d-... --order 3f7a8b2e-...,9c4d1e2f-...,2e6f3a4b-...",
            "umbraco content sort --parent 1a2b3c4d-... --by publishDate --desc   # newest first",
            "umbraco content sort --by name   # the content root, A to Z"
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
                    async (client, c) =>
                    {
                        var parent = parseResult.GetValue(parentOpt);
                        var order = await sort.OrderAsync(
                            parseResult,
                            (skip, take) => client.GetContentAsync(parent, skip, take, c),
                            (child, key) => CandidateAsync(client, child, key, c),
                            c
                        );
                        // The data is the children in their new order (docs/conventions.md 6.2).
                        return order.IsSuccess
                            ? await client
                                .SortContentAsync(parent, order.Data!, c)
                                .Then(ItemRefs.Of(order.Data!))
                            : UmbracoResponse<ItemRefs>.FailureFrom(order);
                    },
                    "Content children reordered.",
                    ct
                )
        );

        // A PUT under a verb the catalog's verb set does not know.
        return cmd.Mutating();
    }

    /// <summary>
    /// A child as a sort candidate. The listing carries the name; the dates need a read of the
    /// child, because the document tree does not carry them. A culture-variant document is dated
    /// by its first variant.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="child">The listed child.</param>
    /// <param name="key">What the children are ordered by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The candidate, or the read failure.</returns>
    private static async Task<UmbracoResponse<SortCandidate>> CandidateAsync(
        IUmbracoManagementClient client,
        ContentItemResponse child,
        SortKey key,
        CancellationToken ct
    )
    {
        if (key == SortKey.Name)
            return UmbracoResponse<SortCandidate>.Success(new SortCandidate(child.Id, child.Name));

        var full = await client.GetContentByIdAsync(child.Id, ct);
        if (!full.IsSuccess)
            return UmbracoResponse<SortCandidate>.FailureFrom(full);
        var variant = full.Data?.Variants?.FirstOrDefault();
        return UmbracoResponse<SortCandidate>.Success(
            new SortCandidate(
                child.Id,
                child.Name,
                variant?.CreateDate,
                variant?.UpdateDate,
                variant?.PublishDate
            )
        );
    }
}
