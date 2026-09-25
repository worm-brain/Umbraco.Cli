using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The per-culture publish state carried by a verbatim document body (#223), and the
/// publish/unpublish steps that bring a live document's state to a snapshot's.
///
/// A culture counts as published when its variant's <c>state</c> is <c>Published</c> or
/// <c>PublishedPendingChanges</c>. An invariant document has one variant with a null culture; its
/// steps act on the <see cref="PublishScope.WholeDocument"/> (the API has no wildcard: <c>"*"</c>
/// is the invariant culture, #158).
/// </summary>
public static class ContentPublishState
{
    private const string Published = "Published";
    private const string PendingChanges = "PublishedPendingChanges";

    /// <summary>The steps that converge one document's publish state.</summary>
    /// <param name="Publish">What to publish, or null for nothing.</param>
    /// <param name="Unpublish">What to unpublish, or null for nothing.</param>
    public sealed record Steps(PublishScope? Publish, PublishScope? Unpublish)
    {
        /// <summary>No state change.</summary>
        public static readonly Steps None = new(null, null);

        /// <summary>True when there is nothing to publish or unpublish.</summary>
        public bool IsEmpty => Publish is null && Unpublish is null;
    }

    /// <summary>
    /// The state steps for a document the snapshot has and the target lacks: publish every culture
    /// the snapshot has published. There is nothing live to unpublish.
    /// </summary>
    /// <param name="desired">The snapshot body.</param>
    /// <returns>The steps.</returns>
    public static Steps ForCreate(JsonNode desired) =>
        new(ScopeOf(States(desired).Where(s => IsPublished(s.State)).Select(s => s.Culture)), null);

    /// <summary>
    /// The state steps for a document on both sides. A culture is published when the snapshot has
    /// it published and the target does not have that same content published: the target's
    /// culture is not published, or the body is being updated (which leaves it pending), or the
    /// snapshot's culture is fully published while the target's has pending changes. A culture is
    /// unpublished when the target has it published and the snapshot does not. A snapshot culture
    /// with pending changes whose target is already published is left alone: the snapshot does not
    /// carry the source's published version, so publishing the draft would publish what the source
    /// has not, and a second apply must be a no-op.
    /// </summary>
    /// <param name="desired">The snapshot body.</param>
    /// <param name="live">The live body.</param>
    /// <param name="bodyChanged">Whether apply updates the body first.</param>
    /// <returns>The steps.</returns>
    public static Steps ForMatch(JsonNode desired, JsonNode live, bool bodyChanged)
    {
        // A lookup, unlike a dictionary, takes the invariant document's null culture as a key.
        var liveStates = States(live).ToLookup(s => s.Culture, s => s.State);

        var publish = new List<string?>();
        var desiredPublished = new HashSet<string?>();
        foreach (var (culture, state) in States(desired))
        {
            if (!IsPublished(state))
                continue;
            desiredPublished.Add(culture);

            var liveState = liveStates[culture].FirstOrDefault();
            var needed =
                bodyChanged
                || !IsPublished(liveState)
                || (state == Published && liveState == PendingChanges);
            if (needed)
                publish.Add(culture);
        }

        var unpublish = States(live)
            .Where(s => IsPublished(s.State) && !desiredPublished.Contains(s.Culture))
            .Select(s => s.Culture);

        return new(ScopeOf(publish), ScopeOf(unpublish));
    }

    /// <summary>
    /// Each variant's culture and state, in body order. Segmented variants share their culture's
    /// publish state, so a culture is listed once.
    /// </summary>
    /// <param name="body">A verbatim document body.</param>
    /// <returns>The (culture, state) pairs; culture is null for an invariant document.</returns>
    public static IEnumerable<(string? Culture, string? State)> States(JsonNode? body)
    {
        var seen = new HashSet<string?>();
        foreach (var variant in (body?["variants"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var culture = ContentBodyNormaliser.Text(variant, "culture");
            if (seen.Add(culture))
                yield return (culture, ContentBodyNormaliser.Text(variant, "state"));
        }
    }

    /// <summary>
    /// The scope for a set of cultures: none when empty, the whole document when it is the
    /// invariant (null) culture, else those cultures.
    /// </summary>
    private static PublishScope? ScopeOf(IEnumerable<string?> cultures)
    {
        var list = cultures.ToList();
        if (list.Count == 0)
            return null;
        return list.Contains(null) ? PublishScope.WholeDocument : new([.. list.OfType<string>()]);
    }

    private static bool IsPublished(string? state) => state is Published or PendingChanges;
}
