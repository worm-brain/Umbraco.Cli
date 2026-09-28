using System.CommandLine;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentPublishCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "publish",
            "Publish a content item, making it live. Optionally target specific cultures."
        )
            .WithExamples(
                "umbraco content publish 3f7a8b2e-...",
                "umbraco content publish 3f7a8b2e-... --culture en-US da-DK"
            )
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = ListOption.Strings(
            "--culture",
            "ISO culture codes to publish (e.g. en-US da-DK). Publishes all cultures if omitted."
        );
        var publishAtOpt = new Option<DateTimeOffset?>("--publish-at")
        {
            Description =
                "Schedule the publish for a future time (ISO 8601, e.g. 2026-01-01T09:00:00Z). Publishes now if omitted.",
        };
        var unpublishAtOpt = new Option<DateTimeOffset?>("--unpublish-at")
        {
            Description =
                "Schedule an automatic unpublish at a future time (ISO 8601). Stays published if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);
        cmd.Add(publishAtOpt);
        cmd.Add(unpublishAtOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                var publishAt = parseResult.GetValue(publishAtOpt);
                var unpublishAt = parseResult.GetValue(unpublishAtOpt);
                var id = parseResult.GetValue(idArg);
                return executor.RunMessageAsync(
                    parseResult,
                    (client, c) => PublishAsync(client, id, cultures, publishAt, unpublishAt, c),
                    SuccessMessage(publishAt, unpublishAt, cultures),
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// Resolves the cultures the publish covers, publishes exactly those, and reports them (#325):
    /// with no <c>--culture</c> that is every culture the document has, so the result names them
    /// rather than leaving the field out.
    /// </summary>
    /// <param name="client">The Management API client.</param>
    /// <param name="id">The content item id.</param>
    /// <param name="cultures">The cultures named with <c>--culture</c>, if any.</param>
    /// <param name="publishAt">When the publish is scheduled for, or null to publish now.</param>
    /// <param name="unpublishAt">When an unpublish is scheduled for, or null for none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The publish result, or the failure of resolving the cultures or of the publish.</returns>
    internal static async Task<UmbracoResponse<PublishResult>> PublishAsync(
        IUmbracoManagementClient client,
        Guid id,
        string[]? cultures,
        DateTimeOffset? publishAt,
        DateTimeOffset? unpublishAt,
        CancellationToken ct
    )
    {
        var scope = await client.PublishCulturesAsync(
            id,
            cultures is { Length: > 0 } ? cultures : null,
            ct
        );
        if (!scope.IsSuccess)
            return UmbracoResponse<PublishResult>.FailureFrom(scope);

        // Empty means an invariant document: it is published whole, and says so with a null.
        var named = scope.Data is { Count: > 0 } list ? list : null;
        return await client
            .PublishContentAsync(id, named, publishAt, unpublishAt, ct)
            // The data says what happened, as the message does: a scheduled publish leaves the
            // item as it was until then (#239).
            .Then(
                new PublishResult(
                    id.ToString(),
                    Published: publishAt is null,
                    publishAt,
                    unpublishAt,
                    named
                )
            );
    }

    /// <summary>
    /// The success message for a publish, which says what actually happened. A scheduled
    /// publish leaves the item as it was until the scheduled time, so "published" was untrue
    /// (#239).
    /// </summary>
    /// <param name="publishAt">When the publish is scheduled for, or null to publish now.</param>
    /// <param name="unpublishAt">When an unpublish is scheduled for, or null for none.</param>
    /// <param name="cultures">The cultures named with <c>--culture</c>, if any.</param>
    /// <returns>The message.</returns>
    internal static string SuccessMessage(
        DateTimeOffset? publishAt,
        DateTimeOffset? unpublishAt,
        string[]? cultures
    )
    {
        var message = (publishAt, unpublishAt) switch
        {
            ({ } p, { } u) => $"Scheduled to publish at {Iso(p)} and unpublish at {Iso(u)}",
            ({ } p, null) => $"Scheduled to publish at {Iso(p)}",
            (null, { } u) => $"Content item published; scheduled to unpublish at {Iso(u)}",
            _ => "Content item published",
        };
        var scope = cultures is { Length: > 0 } ? $" ({string.Join(", ", cultures)})" : "";
        return $"{message}{scope}.";
    }

    /// <summary>Formats a time as UTC ISO 8601, e.g. <c>2026-01-01T09:00:00Z</c>.</summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The formatted time.</returns>
    private static string Iso(DateTimeOffset time) =>
        time.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture
        );

    /// <summary>
    /// The data of a publish: the item, whether it is published now or only scheduled, and what it
    /// covered. Every field is always present (#325): an absent field means "unknown" in this CLI's
    /// output contract, so a null here is written out rather than dropped.
    /// </summary>
    /// <param name="Id">The content item's id.</param>
    /// <param name="Published">True when published now; false when the publish is only scheduled.</param>
    /// <param name="PublishAt">When the publish is scheduled for, or null.</param>
    /// <param name="UnpublishAt">When an unpublish is scheduled for, or null.</param>
    /// <param name="Cultures">The cultures published; null for an invariant document, which has none.</param>
    internal sealed record PublishResult(
        string Id,
        bool Published,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? PublishAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? UnpublishAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            IReadOnlyList<string>? Cultures
    );
}
