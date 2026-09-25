using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>An item a reference can match: its id, and whichever of alias and name it has.</summary>
/// <param name="Id">The item id.</param>
/// <param name="Alias">The alias, or null for a kind without one.</param>
/// <param name="Name">The display name (a dictionary item's key), or null.</param>
internal sealed record ReferenceCandidate(Guid Id, string? Alias, string? Name);

/// <summary>
/// The one match rule behind <see cref="IReferenceResolver"/>: alias first, then name, ignoring
/// case; no match is a 404, an ambiguous match a 409 listing the ids.
/// </summary>
internal static class ReferenceMatch
{
    /// <summary>How the messages name a kind: the noun, what it is looked up by, and its list command.</summary>
    private static (string Noun, string By, string List) Describe(EntityKind kind) =>
        kind switch
        {
            EntityKind.Template => ("template", "alias or name", "templates list"),
            EntityKind.DocumentType => ("document type", "alias", "content-types list"),
            EntityKind.MediaType => ("media type", "alias or name", "media-types list"),
            EntityKind.MemberType => ("member type", "alias", "member-types list"),
            EntityKind.DataType => ("data type", "name", "data-types list"),
            EntityKind.UserGroup => ("user group", "alias or name", "user-groups list"),
            EntityKind.MemberGroup => ("member group", "name", "member-groups list"),
            EntityKind.DictionaryItem => ("dictionary item", "key", "dictionary list"),
            _ => (kind.ToString(), "reference", "--help"),
        };

    /// <summary>Picks the one candidate <paramref name="reference"/> names.</summary>
    /// <param name="kind">The kind, for the messages.</param>
    /// <param name="reference">The alias, name or key.</param>
    /// <param name="candidates">Every item of the kind.</param>
    /// <returns>The id.</returns>
    /// <exception cref="ApiException">No match (404), or more than one (409).</exception>
    public static Guid Pick(
        EntityKind kind,
        string reference,
        IEnumerable<ReferenceCandidate> candidates
    )
    {
        var all = candidates.ToList();

        // An alias is the item's stable key, so it wins over a name that happens to be equal.
        var byAlias = Matching(all, c => c.Alias, reference);
        if (byAlias.Count > 0)
            return Single(kind, reference, byAlias);

        var byName = Matching(all, c => c.Name, reference);
        if (byName.Count > 0)
            return Single(kind, reference, byName);

        var (noun, by, list) = Describe(kind);
        throw new ApiException(
            $"No {noun} found with the {by} '{reference}'. Use 'umbraco {list}' to find one, "
                + $"or pass its id."
        )
        {
            ResponseStatusCode = 404,
        };
    }

    /// <summary>The one match, or a 409 naming every match so the caller can pick by id.</summary>
    private static Guid Single(EntityKind kind, string reference, List<ReferenceCandidate> matches)
    {
        if (matches.Count == 1)
            return matches[0].Id;

        var (noun, _, _) = Describe(kind);
        var ids = string.Join(", ", matches.Select(m => $"{m.Id} ({m.Name ?? m.Alias})"));
        throw new ApiException(
            $"'{reference}' matches {matches.Count} {noun}s: {ids}. Pass the id of the one you mean."
        )
        {
            ResponseStatusCode = 409,
        };
    }

    private static List<ReferenceCandidate> Matching(
        List<ReferenceCandidate> all,
        Func<ReferenceCandidate, string?> key,
        string reference
    ) =>
        [
            .. all.Where(c => string.Equals(key(c), reference, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(c => c.Id),
        ];
}
