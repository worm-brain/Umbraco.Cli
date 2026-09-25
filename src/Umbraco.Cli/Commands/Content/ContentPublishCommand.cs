using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentPublishCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "publish",
            "Publish a content item, making it live. Optionally target specific cultures.\n\nExamples:\n  umbraco content publish 3f7a8b2e-...\n  umbraco content publish 3f7a8b2e-... --cultures en-US da-DK"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var culturesOpt = ListOption.Strings(
            "--cultures",
            "ISO culture codes to publish (e.g. en-US da-DK). Publishes all cultures if omitted."
        );
        var publishAtOpt = new Option<DateTimeOffset?>("--publish-at")
        {
            Description =
                "Schedule the publish for a future time (ISO 8601, e.g. 2026-01-01T09:00:00Z). Publishes now if omitted (#90).",
        };
        var unpublishAtOpt = new Option<DateTimeOffset?>("--unpublish-at")
        {
            Description =
                "Schedule an automatic unpublish at a future time (ISO 8601). Stays published if omitted (#90).",
        };
        cmd.Add(idArg);
        cmd.Add(culturesOpt);
        cmd.Add(publishAtOpt);
        cmd.Add(unpublishAtOpt);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var cultures = parseResult.GetValue(culturesOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    "content.publish",
                    (client, c) =>
                        client.PublishContentAsync(
                            parseResult.GetValue(idArg),
                            cultures?.Length > 0 ? cultures : null,
                            parseResult.GetValue(publishAtOpt),
                            parseResult.GetValue(unpublishAtOpt),
                            c
                        ),
                    SuccessMessage(
                        parseResult.GetValue(publishAtOpt),
                        parseResult.GetValue(unpublishAtOpt),
                        cultures
                    ),
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// The success message for a publish, which says what actually happened. A scheduled
    /// publish leaves the item as it was until the scheduled time, so "published" was untrue
    /// (#239).
    /// </summary>
    /// <param name="publishAt">When the publish is scheduled for, or null to publish now.</param>
    /// <param name="unpublishAt">When an unpublish is scheduled for, or null for none.</param>
    /// <param name="cultures">The cultures named with <c>--cultures</c>, if any.</param>
    /// <returns>The message.</returns>
    private static string SuccessMessage(
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
}
