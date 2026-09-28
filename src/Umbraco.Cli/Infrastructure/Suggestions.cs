namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// "Did you mean" suggestions for a value the instance does not know (#278). Presentation, so it
/// lives in the CLI rather than the API client, and is general so any command that refuses an
/// unknown value can reuse it.
/// </summary>
public static class Suggestions
{
    /// <summary>
    /// The candidate closest to <paramref name="typed"/>, or null when nothing is close. Distance
    /// is case-insensitive and, when <paramref name="optionalPrefix"/> is given, also measured
    /// against each candidate without that prefix, so a bare name (<c>ContentPublished</c>) still
    /// finds a prefixed one (<c>Umbraco.ContentPublish</c>). Of equally close candidates the first
    /// wins.
    /// </summary>
    /// <param name="typed">The unknown value as typed.</param>
    /// <param name="candidates">The values the instance knows.</param>
    /// <param name="optionalPrefix">A prefix the caller may leave off, e.g. <c>Umbraco.</c>; null for none.</param>
    /// <returns>The suggestion, or null.</returns>
    public static string? Nearest(
        string typed,
        IEnumerable<string> candidates,
        string? optionalPrefix = null
    )
    {
        var needle = typed.ToLowerInvariant();

        // Close enough to be a typo or a tense slip, not a different value: a third of the typed
        // length, with a floor so short names still get a suggestion.
        var threshold = Math.Max(2, typed.Length / 3);

        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var lower = candidate.ToLowerInvariant();
            var distance = EditDistance(needle, lower);
            if (
                optionalPrefix is not null
                && lower.StartsWith(optionalPrefix, StringComparison.OrdinalIgnoreCase)
            )
                distance = Math.Min(distance, EditDistance(needle, lower[optionalPrefix.Length..]));
            if (distance < bestDistance)
                (best, bestDistance) = (candidate, distance);
        }

        return bestDistance <= threshold ? best : null;
    }

    /// <summary>Levenshtein distance between two strings (insert, delete, substitute all cost 1).</summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The number of single-character edits that turn <paramref name="a"/> into <paramref name="b"/>.</returns>
    public static int EditDistance(string a, string b)
    {
        // Two-row dynamic programme: row i holds the distances from a[..i] to every prefix of b.
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
