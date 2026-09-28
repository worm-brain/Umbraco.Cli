using System.CommandLine;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content unpublish</c> command.</summary>
public static class ContentUnpublishCommand
{
    /// <summary>
    /// Builds the <c>content unpublish</c> command. Unpublishing takes live content offline, a
    /// high production-impact action, so it is gated by a confirmation prompt (requires
    /// <c>--yes</c> non-interactively) even though it is reversible by re-publishing (issue #82).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "unpublish",
            "Unpublish a content item, taking it offline.\n\nOptionally target specific cultures."
        )
            .WithExamples(
                "umbraco content unpublish 3f7a8b2e-...",
                "umbraco content unpublish 3f7a8b2e-... --culture en-US"
            )
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = ListOption.Strings(
            "--culture",
            "ISO culture codes to unpublish. Unpublishes all cultures if omitted."
        );
        cmd.Add(idArg);
        cmd.Add(culturesOpt);

        cmd.Destructive(parseResult =>
            $"Unpublish content {parseResult.GetValue(idArg)}, taking it offline? Re-publish to restore."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                var id = parseResult.GetValue(idArg);
                return executor.RunMessageAsync(
                    parseResult,
                    (client, c) => UnpublishAsync(client, id, cultures, c),
                    "Content item unpublished.",
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// Resolves the cultures the unpublish covers, unpublishes exactly those, and reports them -
    /// the unpublish side of #325. With no <c>--culture</c> a variant document is unpublished in
    /// every culture it has, so the result names them rather than leaving the field out. The
    /// cultures come from the same resolver as <c>content publish</c>
    /// (<see cref="IContentClient.PublishCulturesAsync"/>), so the two commands report alike, but
    /// only the cultures that were published count (#362): one already in Draft is not listed.
    /// </summary>
    /// <param name="client">The Management API client.</param>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">The cultures named with <c>--culture</c>, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unpublish result, or the failure of resolving the cultures or of the unpublish.</returns>
    internal static async Task<UmbracoResponse<UnpublishResult>> UnpublishAsync(
        IUmbracoManagementClient client,
        Guid id,
        string[]? cultures,
        CancellationToken ct
    )
    {
        // A document that cannot be read (including an empty 200, #119) fails here, before any
        // unpublish is sent.
        var requested = cultures is { Length: > 0 } ? cultures : null;
        var scope = await client.PublishCulturesAsync(id, requested, publishedOnly: true, ct: ct);
        if (!scope.IsSuccess)
            return UmbracoResponse<UnpublishResult>.FailureFrom(scope);

        // Null: an invariant document, even when --culture named one (#362); it is unpublished
        // whole and reports null. Otherwise the cultures that were live, which are what go offline.
        // When none of them was live the request goes out as asked, so Umbraco still answers for
        // it, and the result reports that nothing was taken offline ([]).
        var live = scope.Data;
        var send = live is { Count: 0 } ? requested : live;
        return await client
            .UnpublishContentAsync(id, send, ct)
            .Then(new UnpublishResult(id.ToString(), live));
    }

    /// <summary>
    /// The data of an unpublish: the item and the cultures taken offline. <c>cultures</c> is always
    /// written (#325): an absent field means "unknown" in this CLI's output contract, so the null
    /// of an invariant document is written out rather than dropped.
    /// </summary>
    /// <param name="Id">The content item's id.</param>
    /// <param name="Cultures">
    /// The cultures taken offline (empty when none was published); null for an invariant document,
    /// which has none.
    /// </param>
    internal sealed record UnpublishResult(
        string Id,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            IReadOnlyList<string>? Cultures
    );
}
