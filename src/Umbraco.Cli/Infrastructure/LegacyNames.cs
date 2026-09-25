using System.CommandLine;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Command names renamed by the 2026-09-25 surface batch (#268, docs/conventions.md), still
/// accepted for one release. The table is the one place the old names live: the parser rewrite,
/// the deprecation warning and the allow-list all read it, so they cannot disagree. The old names
/// are hidden - not in <c>--help</c>, <c>umbraco commands</c> or <c>docs/surface.json</c> - so
/// nothing new is written against them.
/// </summary>
public static class LegacyNames
{
    /// <summary>A renamed command: under <see cref="Parent"/> (dotted, "" for the root), <see cref="Old"/> became <see cref="New"/>.</summary>
    /// <param name="Parent">The dotted path of the parent command; empty for a top-level noun.</param>
    /// <param name="Old">The old name.</param>
    /// <param name="New">The current name.</param>
    private sealed record Rename(string Parent, string Old, string New);

    private static readonly Rename[] Renames =
    [
        new("", "content-types", "document-type"),
        new("", "media-types", "media-type"),
        new("", "data-types", "data-type"),
        new("", "languages", "language"),
        new("", "templates", "template"),
        new("", "members", "member"),
        new("", "member-types", "member-type"),
        new("", "users", "user"),
        new("", "webhooks", "webhook"),
        new("", "member-groups", "member-group"),
        new("", "tags", "tag"),
        new("", "cultures", "culture"),
        new("", "user-groups", "user-group"),
        new("content", "domains", "domain"),
    ];

    /// <summary>
    /// When <paramref name="parsed"/> failed on an old name, returns <paramref name="args"/> with
    /// that name replaced, and the warning to print. One rename per call; the caller re-parses and
    /// asks again, so a command line with an old noun and an old sub-noun is rewritten in turn.
    /// </summary>
    /// <param name="parsed">The parse of <paramref name="args"/>.</param>
    /// <param name="args">The command line as given.</param>
    /// <param name="rewritten">The command line with the old name replaced.</param>
    /// <param name="warning">The deprecation warning naming both names.</param>
    /// <returns>True when an old name was found and replaced.</returns>
    public static bool TryRewrite(
        ParseResult parsed,
        string[] args,
        out string[] rewritten,
        out string warning
    )
    {
        rewritten = args;
        warning = "";
        // An old name is never a valid token, so it is always the first unmatched one. A top-level
        // noun stops the parser at the root; an old sub-noun is skipped and the parser carries on
        // to the verb after it (`content domains get` reaches `content get`), so a sub-noun only
        // needs the reached command to sit under its parent.
        if (parsed.Errors.Count == 0 || parsed.UnmatchedTokens.Count == 0)
            return false;

        var reached = CommandPath.Of(parsed) ?? "";
        var token = parsed.UnmatchedTokens[0];
        var rename = Array.Find(
            Renames,
            r =>
                r.Old == token
                && (
                    r.Parent.Length == 0
                        ? reached.Length == 0
                        : reached == r.Parent || reached.StartsWith(r.Parent + ".")
                )
        );
        var index = Array.IndexOf(args, token);
        if (rename is null || index < 0)
            return false;

        rewritten = [.. args];
        rewritten[index] = rename.New;
        warning =
            $"warning: '{Path(rename.Parent, rename.Old)}' is now '{Path(rename.Parent, rename.New)}'. "
            + "The old name will stop working in the next release.";
        return true;
    }

    /// <summary>
    /// The current form of an allow-list entry (a noun group or a dotted command name), so a
    /// supervisor's existing <c>UMBRACO_ALLOWED_COMMANDS=content-types</c> keeps allowing what it
    /// allowed before the rename, and never allows more.
    /// </summary>
    /// <param name="entry">An allow-list entry, e.g. <c>content-types</c> or <c>content.domains.set</c>.</param>
    /// <returns>The entry with any renamed segments replaced.</returns>
    public static string Canonical(string entry)
    {
        var segments = entry.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var parent = string.Join('.', segments[..i]);
            var rename = Array.Find(
                Renames,
                r =>
                    string.Equals(r.Parent, parent, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Old, segments[i], StringComparison.OrdinalIgnoreCase)
            );
            if (rename is not null)
                segments[i] = rename.New;
        }
        return string.Join('.', segments);
    }

    private static string Path(string parent, string name) =>
        parent.Length == 0 ? name : $"{parent.Replace('.', ' ')} {name}";
}
