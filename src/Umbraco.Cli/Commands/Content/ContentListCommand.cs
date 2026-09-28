using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content list</c> command.</summary>
public static class ContentListCommand
{
    /// <summary>
    /// Builds the <c>content list</c> command: one level of the content tree, or with
    /// <c>--trashed</c> one level of the recycle bin (#364), so what <c>empty-recycle-bin</c> would
    /// delete can be seen first and a trashed id found to <c>restore</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List content items at the root, or the children of a parent.\n\nReturns a paginated list of top-level or child content nodes. With --trashed, lists the recycle bin instead."
        ).WithExamples(
            "umbraco content list",
            "umbraco content list --parent <id> --take 50",
            "umbraco content list --trashed   # what empty-recycle-bin would delete",
            "umbraco content list --output json | jq '.data[].name'"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent content item id; lists root items if omitted. With --trashed, a trashed item.",
        };
        var trashedOpt = new Option<bool>("--trashed")
        {
            Description = "List the recycle bin (its top level, or a trashed parent's children).",
        };
        cmd.Add(parentOpt);
        cmd.Add(trashedOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        Page(
                            client,
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(trashedOpt),
                            skip,
                            take,
                            c
                        ),
                    // Content Type is intentionally omitted: the document-tree list items carry
                    // only the type id (no alias), so the column was always blank (#75). Use
                    // 'content get <id>' for the full content type.
                    ["ID", "Name", "Published"],
                    i => new[] { i.Id.ToString(), i.Name, i.IsPublished.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }

    /// <summary>Reads one page of the content tree, or of the recycle bin with <paramref name="trashed"/>.</summary>
    /// <param name="client">The Management API client.</param>
    /// <param name="parent">The parent whose children to list; null for the top level.</param>
    /// <param name="trashed">True to list the recycle bin instead of the content tree.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, or a mapped failure.</returns>
    private static Task<UmbracoResponse<PagedResponse<ContentItemResponse>>> Page(
        IUmbracoManagementClient client,
        Guid? parent,
        bool trashed,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        trashed
            ? client.GetContentRecycleBinAsync(parent, skip, take, ct)
            : client.GetContentAsync(parent, skip, take, ct);
}
