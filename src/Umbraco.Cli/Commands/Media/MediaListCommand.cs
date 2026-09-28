using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media list</c> command.</summary>
public static class MediaListCommand
{
    /// <summary>
    /// Builds the <c>media list</c> command: one level of the media tree, or with
    /// <c>--trashed</c> one level of the media recycle bin (#364).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List media items in the media library.\n\nWith --trashed, lists the media recycle bin instead."
        ).WithExamples(
            "umbraco media list",
            "umbraco media list --parent <folder-id> --output json",
            "umbraco media list --trashed   # what empty-recycle-bin would delete"
        );
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description =
                "Parent media folder id; lists root media items if omitted. With --trashed, a trashed item.",
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
                    // Media Type is intentionally omitted: the media-tree list items carry only
                    // the type id (no alias), so the column was always blank (#75). Use
                    // 'media get <id>' for the full media type.
                    ["ID", "Name"],
                    i => new[] { i.Id.ToString(), i.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }

    /// <summary>Reads one page of the media tree, or of the recycle bin with <paramref name="trashed"/>.</summary>
    /// <param name="client">The Management API client.</param>
    /// <param name="parent">The parent whose children to list; null for the top level.</param>
    /// <param name="trashed">True to list the recycle bin instead of the media tree.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, or a mapped failure.</returns>
    private static Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> Page(
        IUmbracoManagementClient client,
        Guid? parent,
        bool trashed,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        trashed
            ? client.GetMediaRecycleBinAsync(parent, skip, take, ct)
            : client.GetMediaAsync(parent, skip, take, ct);
}
