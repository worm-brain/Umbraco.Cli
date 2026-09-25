using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The per-culture publish state carried by a verbatim document body (#223), and the
/// publish/unpublish steps that bring a live document's state to a snapshot's.
///
/// A culture counts as published when its variant's <c>state</c> is <c>Published</c> or
/// <c>PublishedPendingChanges</c>. An invariant document has one variant with a null culture; it is
/// represented here by a null culture, which the applier turns into a whole-document call (the
/// API has no wildcard: <c>"*"</c> is the invariant culture, #158).
/// </summary>
public static class ContentPublishState
{
    private const string Published = "Published";
    private const string PendingChanges = "PublishedPendingChanges";

    /// <summary>The steps that converge one document's publish state.</summary>
    /// <param name="Publish">Cultures to publish, in body order; null inside means invariant.</param>
    /// <param name="Unpublish">Cultures to unpublish; null inside means invariant.</param>
    public sealed record Steps(IReadOnlyList<string?> Publish, IReadOnlyList<string?> Unpublish)
    {
        /// <summary>No state change.</summary>
        public static readonly Steps None = new([], []);

        /// <summary>True when there is nothing to publish or unpublish.</summary>
        public bool IsEmpty => Publish.Count == 0 && Unpublish.Count == 0;
    }

    /// <summary>
    /// The state steps for a document the snapshot has and the target lacks: publish every culture
    /// the snapshot has published. There is nothing live to unpublish.
    /// </summary>
    /// <param name="desired">The snapshot body.</param>
    /// <returns>The steps.</returns>
    public static Steps ForCreate(JsonNode desired) =>
        new([.. States(desired).Where(s => IsPublished(s.State)).Select(s => s.Culture)], []);

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
        var liveStates = new Dictionary<string, string?>();
        foreach (var (culture, state) in States(live))
            liveStates[Key(culture)] = state;

        var publish = new List<string?>();
        var desiredPublished = new HashSet<string>();
        foreach (var (culture, state) in States(desired))
        {
            if (!IsPublished(state))
                continue;
            desiredPublished.Add(Key(culture));

            var liveState = liveStates.GetValueOrDefault(Key(culture));
            var needed =
                bodyChanged
                || !IsPublished(liveState)
                || (state == Published && liveState == PendingChanges);
            if (needed)
                publish.Add(culture);
        }

        var unpublish = States(live)
            .Where(s => IsPublished(s.State) && !desiredPublished.Contains(Key(s.Culture)))
            .Select(s => s.Culture)
            .ToList();

        return publish.Count == 0 && unpublish.Count == 0 ? Steps.None : new(publish, unpublish);
    }

    /// <summary>
    /// Each variant's culture and state, in body order. Segmented variants share their culture's
    /// publish state, so a culture is listed once.
    /// </summary>
    /// <param name="body">A verbatim document body.</param>
    /// <returns>The (culture, state) pairs; culture is null for an invariant document.</returns>
    public static IEnumerable<(string? Culture, string? State)> States(JsonNode? body)
    {
        var seen = new HashSet<string>();
        foreach (var variant in (body?["variants"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var culture = ContentBodyNormaliser.Text(variant, "culture");
            if (seen.Add(Key(culture)))
                yield return (culture, ContentBodyNormaliser.Text(variant, "state"));
        }
    }

    private static bool IsPublished(string? state) => state is Published or PendingChanges;

    // A null culture cannot be a dictionary key; no real culture code is empty.
    private static string Key(string? culture) => culture ?? "";
}
