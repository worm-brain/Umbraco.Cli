using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Redirects;

/// <summary>
/// Wires the <c>redirect</c> noun (issue #118): list tracked URL redirects (optionally for one
/// document or filtered), show the tracking status, delete a redirect, and toggle URL tracking.
/// <c>delete</c> and the tracking toggle are confirmation-gated.
/// </summary>
public static class RedirectCommand
{
    /// <summary>Builds the <c>redirect</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "redirect",
            "List and manage tracked URL redirects.\n\nExamples:\n  umbraco redirect list --filter old-page\n  umbraco redirect status"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildStatus(executor));
        cmd.Add(BuildDelete(executor));
        cmd.Add(BuildTracking(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List redirects. With --content, lists redirects pointing at that document."
        );
        var contentOpt = new Option<Guid?>("--content")
        {
            Description = "List redirects for this destination document (content key).",
        };
        var filterOpt = new Option<string?>("--filter")
        {
            Description = "Filter redirects by URL text (ignored with --content).",
        };
        cmd.Add(contentOpt);
        cmd.Add(filterOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "redirect.list",
                    (client, skip, take, c) =>
                    {
                        return parseResult.GetValue(contentOpt) is { } key
                            ? client.GetRedirectsForContentAsync(key, skip, take, c)
                            : client.GetRedirectsAsync(
                                parseResult.GetValue(filterOpt),
                                skip,
                                take,
                                c
                            );
                    },
                    new[] { "Id", "OriginalUrl", "DestinationUrl", "Culture" },
                    r =>
                        new[] { r.Id.ToString(), r.OriginalUrl, r.DestinationUrl, r.Culture ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildStatus(CommandExecutor executor)
    {
        var cmd = new Command("status", "Show whether automatic URL-redirect tracking is enabled.");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "redirect.status",
                    (client, c) => client.GetRedirectStatusAsync(c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a redirect by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Redirect ID." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult => $"Delete redirect {parseResult.GetValue(idArg)}?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "redirect.delete",
                    (client, c) => client.DeleteRedirectAsync(parseResult.GetValue(idArg), c),
                    "Redirect deleted.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildTracking(CommandExecutor executor)
    {
        var cmd = new Command("tracking", "Enable or disable automatic URL-redirect tracking.");
        cmd.Add(BuildTrackingToggle(executor, "enable", enabled: true));
        cmd.Add(BuildTrackingToggle(executor, "disable", enabled: false));
        return cmd;
    }

    /// <summary>Builds a tracking <c>enable</c>/<c>disable</c> verb (a site-wide, confirmation-gated toggle).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="verb">The verb name (<c>enable</c> or <c>disable</c>).</param>
    /// <param name="enabled">The tracking state the verb sets.</param>
    /// <returns>The configured verb command.</returns>
    private static Command BuildTrackingToggle(CommandExecutor executor, string verb, bool enabled)
    {
        var cmd = new Command(verb, $"{(enabled ? "Enable" : "Disable")} URL-redirect tracking.");
        // Only disabling is gated: it stops Umbraco recording redirects site-wide, so moved pages
        // start to 404. Enabling turns a protection on and needs no --yes (#249).
        if (!enabled)
            cmd.Destructive(_ => "Disable URL-redirect tracking site-wide?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    $"redirect.tracking.{verb}",
                    (client, c) => client.SetRedirectTrackingAsync(enabled, c),
                    $"URL-redirect tracking {(enabled ? "enabled" : "disabled")}.",
                    ct
                )
        );
        return cmd;
    }
}
