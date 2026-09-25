using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// One create/update/delete step in a content apply run (issue #100), as reported to the user. For
/// a dry run <see cref="Status"/> is <c>planned</c>; for a real run it is <c>success</c> (the
/// executed steps up to any failure) - apply is fail-fast, so a failure surfaces through the exit
/// code and message rather than a per-step error row.
/// </summary>
/// <param name="Change">The change kind: <see cref="ContentChangeKind.Added"/> (create), <see cref="ContentChangeKind.Changed"/> (update), or <see cref="ContentChangeKind.Removed"/> (delete).</param>
/// <param name="Id">The document id the step targets.</param>
/// <param name="Status">The step status: <c>planned</c> (dry run) or <c>success</c> (executed).</param>
public sealed record ContentAction(ContentChangeKind Change, Guid Id, string Status)
{
    /// <summary>The user-facing verb for this step (<c>create</c>/<c>update</c>/<c>delete</c>).</summary>
    public string Operation => ContentApplier.VerbOf(Change);
}

/// <summary>
/// The outcome of a content apply run (issue #100): whether it was a dry run, whether prune was
/// on, and the ordered steps (planned or executed). Counts are derived for a quick summary.
/// </summary>
/// <param name="DryRun">True when nothing was written (a plan preview).</param>
/// <param name="Pruned">True when <c>--prune</c> was in effect (deletes were part of the plan).</param>
/// <param name="Actions">The ordered create/update/delete steps.</param>
public sealed record ContentApplyResult(
    bool DryRun,
    bool Pruned,
    IReadOnlyList<ContentAction> Actions
)
{
    /// <summary>Number of create steps.</summary>
    public int Created => Actions.Count(a => a.Change == ContentChangeKind.Added);

    /// <summary>Number of update steps.</summary>
    public int Updated => Actions.Count(a => a.Change == ContentChangeKind.Changed);

    /// <summary>Number of delete steps.</summary>
    public int Deleted => Actions.Count(a => a.Change == ContentChangeKind.Removed);
}

/// <summary>
/// Executes (or, under dry run, plans) the changes a <see cref="ContentDiff"/> describes
/// (issue #100 / ADR 0006). It never computes a diff itself - the command feeds it one - so the
/// ordering/execution logic is testable in isolation.
///
/// Creates run in snapshot pre-order (the diff preserves it), so a parent is always created before
/// its children; each create injects the document's captured parent into the body (the raw body
/// does not carry it). Deletes (prune) run in reverse, deepest-first, so a parent is not removed
/// while it still has children. Apply is <b>fail-fast</b>: the first failed write stops the run.
///
/// Apply replaces document bodies and creates new documents in place; it does <b>not</b> re-parent
/// existing documents (placement drift is reported by <c>diff</c> but a move is out of scope).
/// </summary>
/// <summary>
/// What a content prune must leave alone (#225): documents of these types, and documents at or
/// under these roots. A whole-tree prune otherwise deletes everything created on the target
/// since the export, such as form submissions.
/// </summary>
/// <param name="DocumentTypeIds">Document types whose documents are never pruned.</param>
/// <param name="Roots">Documents that are never pruned, together with everything under them.</param>
public sealed record PruneExclusions(IReadOnlySet<Guid> DocumentTypeIds, IReadOnlySet<Guid> Roots)
{
    /// <summary>No exclusions.</summary>
    public static PruneExclusions None { get; } = new(new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>Whether there is nothing to exclude.</summary>
    public bool IsEmpty => DocumentTypeIds.Count == 0 && Roots.Count == 0;
}

public static class ContentApplier
{
    /// <summary>Maps a change kind to its user-facing apply verb.</summary>
    /// <param name="change">The change kind (must be an actionable one: added/changed/removed).</param>
    /// <returns>The verb: <c>create</c>, <c>update</c>, or <c>delete</c>.</returns>
    public static string VerbOf(ContentChangeKind change) =>
        change switch
        {
            ContentChangeKind.Added => "create",
            ContentChangeKind.Changed => "update",
            ContentChangeKind.Removed => "delete",
            _ => throw new InvalidOperationException(
                $"{change} is not an actionable apply operation."
            ),
        };

    /// <summary>
    /// Applies <paramref name="diff"/> to the live instance, or (when <paramref name="dryRun"/>)
    /// returns the plan without writing anything.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff to apply (already computed against the live instance).</param>
    /// <param name="prune">When true, delete live documents the snapshot matched nothing to.</param>
    /// <param name="dryRun">When true, compute the plan and write nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="exclude">Documents a prune must leave alone (#225); null for none.</param>
    /// <returns>The apply result, or the first write failure.</returns>
    public static async Task<UmbracoResponse<ContentApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        ContentDiff diff,
        bool prune,
        bool dryRun,
        CancellationToken ct,
        PruneExclusions? exclude = null
    )
    {
        var plan = BuildPlan(diff, prune, exclude ?? PruneExclusions.None);

        if (dryRun)
        {
            var planned = plan.Select(c => new ContentAction(c.Change, c.Id, "planned")).ToList();
            return UmbracoResponse<ContentApplyResult>.Success(
                new ContentApplyResult(DryRun: true, Pruned: prune, planned)
            );
        }

        var done = new List<ContentAction>();
        foreach (var change in plan)
        {
            var result = await Execute(client, change, ct);
            if (!result.IsSuccess)
            {
                var doneSummary =
                    done.Count == 0 ? "no changes were applied" : $"{done.Count} change(s) applied";
                return UmbracoResponse<ContentApplyResult>.Failure(
                    result.StatusCode,
                    $"Apply failed on {VerbOf(change.Change)} document '{change.Id}' "
                        + $"({doneSummary} before the failure): {result.ErrorMessage}"
                );
            }
            done.Add(new ContentAction(change.Change, change.Id, "success"));
        }

        return UmbracoResponse<ContentApplyResult>.Success(
            new ContentApplyResult(DryRun: false, Pruned: prune, done)
        );
    }

    /// <summary>
    /// Builds the ordered change list from a diff: creates in snapshot pre-order (parents first),
    /// then updates, then - if pruning - deletes in reverse (deepest documents first, so a parent
    /// is never deleted while it still has children). Drift is never planned (apply cannot move
    /// documents).
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="prune">Whether to include deletes.</param>
    /// <param name="exclude">Documents the deletes must leave alone.</param>
    /// <returns>The ordered changes to execute.</returns>
    private static List<ContentDocumentChange> BuildPlan(
        ContentDiff diff,
        bool prune,
        PruneExclusions exclude
    )
    {
        // Creates preserve the snapshot's pre-order (parent before child); nothing to topo-sort.
        var plan = new List<ContentDocumentChange>(diff.Added);
        plan.AddRange(diff.Changed);

        // Deletes in reverse tree order: the Removed list is in live pre-order (parents first), so
        // reversing it deletes children before their parents. Fail-fast covers any residual order
        // issue (a re-run completes once the blocker is gone).
        if (prune)
        {
            var kept = KeptByExclusions(diff, exclude);
            for (var i = diff.Removed.Count - 1; i >= 0; i--)
                if (!kept.Contains(diff.Removed[i].Id))
                    plan.Add(diff.Removed[i]);
        }

        return plan;
    }

    /// <summary>
    /// The removed documents a prune must keep (#225): those of an excluded type, those at or
    /// under an excluded root, and every removed ancestor of either - Umbraco deletes a document's
    /// descendants with it, so deleting the parent would take the excluded child anyway.
    /// </summary>
    /// <param name="diff">The diff, with the live parent map.</param>
    /// <param name="exclude">The exclusions.</param>
    /// <returns>The ids of removed documents to keep.</returns>
    private static HashSet<Guid> KeptByExclusions(ContentDiff diff, PruneExclusions exclude)
    {
        var kept = new HashSet<Guid>();
        if (exclude.IsEmpty)
            return kept;

        var removedIds = diff.Removed.Select(r => r.Id).ToHashSet();
        foreach (var removed in diff.Removed)
        {
            var excluded =
                removed.DocumentTypeId is { } type && exclude.DocumentTypeIds.Contains(type)
                || Lineage(removed.Id, diff.LiveParents).Any(exclude.Roots.Contains);
            if (!excluded)
                continue;

            // Keep it, and every removed ancestor, so no delete cascades down onto it.
            foreach (var id in Lineage(removed.Id, diff.LiveParents))
                if (removedIds.Contains(id))
                    kept.Add(id);
        }
        return kept;
    }

    /// <summary>A document id followed by its ancestors' ids, nearest first.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="parents">Every live document's parent.</param>
    /// <returns>The id and its ancestors.</returns>
    private static IEnumerable<Guid> Lineage(Guid id, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        // The guard stops a cycle in a malformed snapshot from looping forever.
        var seen = new HashSet<Guid>();
        for (
            Guid? current = id;
            current is { } c && seen.Add(c);
            current = parents.GetValueOrDefault(c)
        )
            yield return c;
    }

    /// <summary>Runs a single change against the client, dispatched on its kind.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="change">The change to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write result.</returns>
    private static Task<UmbracoResponse<Empty>> Execute(
        IUmbracoManagementClient client,
        ContentDocumentChange change,
        CancellationToken ct
    ) =>
        change.Change switch
        {
            // Create carries the document's own id (GUID-primary) already in the body; inject the
            // captured parent so it lands in the right place.
            ContentChangeKind.Added => client.CreateDocumentRawAsync(
                WithParent(change.DesiredBody!, change.Parent),
                ct
            ),
            ContentChangeKind.Changed => client.UpdateDocumentRawAsync(
                change.Id,
                change.DesiredBody!,
                ct
            ),
            ContentChangeKind.Removed => client.DeleteContentAsync(change.Id, ct),
            _ => throw new InvalidOperationException(
                $"{change.Change} is not an executable apply operation."
            ),
        };

    /// <summary>
    /// Returns a clone of <paramref name="body"/> with its top-level <c>parent</c> set for a create:
    /// <c>{ "id": &lt;parent&gt; }</c>, or <c>null</c> for a content-root document. The raw GET body
    /// does not carry a parent, so create supplies it here. The original node is not mutated (it may
    /// be shared with the diff output).
    /// </summary>
    /// <param name="body">The snapshot document body.</param>
    /// <param name="parent">The parent id, or null for the content root.</param>
    /// <returns>A cloned body carrying the parent reference.</returns>
    private static JsonNode WithParent(JsonNode body, Guid? parent)
    {
        var clone = body.DeepClone();
        clone["parent"] = parent is { } p ? new JsonObject { ["id"] = p.ToString() } : null;
        return clone;
    }
}
