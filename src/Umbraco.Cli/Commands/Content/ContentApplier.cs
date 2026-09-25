using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// One step in a content apply run (issue #100), as reported to the user. For a dry run
/// <see cref="Status"/> is <c>planned</c>; for a real run it is <c>success</c> (the executed steps
/// up to any failure) - apply is fail-fast, so a failure surfaces through the exit code and message
/// rather than a per-step error row.
/// </summary>
/// <param name="Operation">The step.</param>
/// <param name="Id">The document id the step targets.</param>
/// <param name="Status">The step status: <c>planned</c> (dry run) or <c>success</c> (executed).</param>
public sealed record ContentAction(ContentOperation Operation, Guid Id, string Status)
{
    /// <summary>
    /// For a publish or unpublish of a culture-variant document, the cultures it acts on. Null for
    /// every other step, and for an invariant document (the call covers the whole document).
    /// Always serialized, as null when empty (#229).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<string>? Cultures { get; init; }
}

/// <summary>
/// The outcome of a content apply run (issue #100): whether it was a dry run, whether prune was
/// on, and the ordered steps (planned or executed). Counts are derived for a quick summary.
/// </summary>
/// <param name="DryRun">True when nothing was written (a plan preview).</param>
/// <param name="Pruned">True when <c>--prune</c> was in effect (deletes were part of the plan).</param>
/// <param name="Actions">The ordered steps.</param>
public sealed record ContentApplyResult(
    bool DryRun,
    bool Pruned,
    IReadOnlyList<ContentAction> Actions
)
{
    /// <summary>Number of create steps.</summary>
    public int Created => Count(ContentOperation.Create);

    /// <summary>Number of update steps.</summary>
    public int Updated => Count(ContentOperation.Update);

    /// <summary>Number of publish steps (one per document, whatever its cultures).</summary>
    public int Published => Count(ContentOperation.Publish);

    /// <summary>Number of unpublish steps.</summary>
    public int Unpublished => Count(ContentOperation.Unpublish);

    /// <summary>Number of delete steps.</summary>
    public int Deleted => Count(ContentOperation.Delete);

    private int Count(ContentOperation operation) => Actions.Count(a => a.Operation == operation);
}

/// <summary>
/// Executes (or, under dry run, plans) the changes a <see cref="ContentDiff"/> describes
/// (issue #100 / ADR 0006). It never computes a diff itself - the command feeds it one - so the
/// ordering/execution logic is testable in isolation.
///
/// Creates run in snapshot pre-order (the diff preserves it), so a parent is always created before
/// its children; each create injects the document's captured parent into the body (the raw body
/// does not carry it). Publish state follows the bodies (#223): unpublishes deepest-first, then
/// publishes in snapshot pre-order, because Umbraco will not publish a document under an
/// unpublished parent. Deletes (prune) run last, in reverse, deepest-first, so a parent is not
/// removed while it still has children. Apply is <b>fail-fast</b>: the first failed write stops the
/// run.
///
/// Apply replaces document bodies and creates new documents in place; it does <b>not</b> re-parent
/// existing documents (placement drift is reported by <c>diff</c> but a move is out of scope).
/// </summary>
public static class ContentApplier
{
    /// <summary>One planned write: what to do, to which document, and for which cultures.</summary>
    /// <param name="Operation">The step.</param>
    /// <param name="Change">The diff entry the step came from.</param>
    /// <param name="Scope">For a (un)publish, what it acts on.</param>
    private sealed record Step(
        ContentOperation Operation,
        ContentDocumentChange Change,
        PublishScope? Scope = null
    )
    {
        /// <summary>The step as reported.</summary>
        /// <param name="status">The status to report.</param>
        /// <returns>The action row.</returns>
        public ContentAction ToAction(string status) =>
            new(Operation, Change.Id, status) { Cultures = Scope?.Cultures };
    }

    /// <summary>
    /// Applies <paramref name="diff"/> to the live instance, or (under a dry run)
    /// returns the plan without writing anything.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff to apply (already computed against the live instance).</param>
    /// <param name="options">Whether to prune (and what to leave alone), and whether to write.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The apply result, or the first write failure.</returns>
    public static async Task<UmbracoResponse<ContentApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        ContentDiff diff,
        ContentApplyOptions options,
        CancellationToken ct
    )
    {
        var (prune, dryRun) = options;
        var plan = BuildPlan(diff, options);

        if (dryRun)
        {
            var planned = plan.Select(s => s.ToAction("planned")).ToList();
            return UmbracoResponse<ContentApplyResult>.Success(
                new ContentApplyResult(DryRun: true, Pruned: prune, planned)
            );
        }

        var done = new List<ContentAction>();
        foreach (var step in plan)
        {
            var result = await Execute(client, step, ct);
            if (!result.IsSuccess)
            {
                var doneSummary =
                    done.Count == 0 ? "no changes were applied" : $"{done.Count} change(s) applied";
                return UmbracoResponse<ContentApplyResult>.Failure(
                    result.StatusCode,
                    $"Apply failed on {step.Operation.ToString().ToLowerInvariant()} document '{step.Change.Id}' "
                        + $"({doneSummary} before the failure): {result.ErrorMessage}"
                );
            }
            done.Add(step.ToAction("success"));
        }

        return UmbracoResponse<ContentApplyResult>.Success(
            new ContentApplyResult(DryRun: false, Pruned: prune, done)
        );
    }

    /// <summary>
    /// Builds the ordered steps from a diff: creates in snapshot pre-order (parents first), then
    /// updates of changed bodies, then - unless state is off - unpublishes deepest-first and
    /// publishes parents-first, then - if pruning - deletes in reverse (deepest documents first, so
    /// a parent is never deleted while it still has children). Drift is never planned (apply cannot
    /// move documents).
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="options">Whether to carry state, and whether (and what not) to prune.</param>
    /// <returns>The ordered steps to execute.</returns>
    private static List<Step> BuildPlan(ContentDiff diff, ContentApplyOptions options)
    {
        // The diff's documents are in snapshot pre-order (parent before child), so every pass is
        // a walk of that one list - forwards where parents go first, backwards where children do.
        var documents = diff.Documents;
        var plan = new List<Step>();
        plan.AddRange(
            documents
                .Where(d => d.Change == ContentChangeKind.Added)
                .Select(d => new Step(ContentOperation.Create, d))
        );
        plan.AddRange(
            documents
                .Where(d => d.Change == ContentChangeKind.Changed && d.BodyChanged)
                .Select(d => new Step(ContentOperation.Update, d))
        );

        if (options.State)
        {
            for (var i = documents.Count - 1; i >= 0; i--)
                if (documents[i].State.Unpublish is { } unpublish)
                    plan.Add(new Step(ContentOperation.Unpublish, documents[i], unpublish));
            foreach (var d in documents)
                if (d.State.Publish is { } publish)
                    plan.Add(new Step(ContentOperation.Publish, d, publish));
        }

        // Deletes in reverse tree order: removed documents are in live pre-order (parents first),
        // so reversing deletes children before their parents. Fail-fast covers any residual order
        // issue (a re-run completes once the blocker is gone).
        if (options.Prune)
        {
            var kept = KeptByExclusions(diff, options.Exclude);
            for (var i = documents.Count - 1; i >= 0; i--)
                if (
                    documents[i].Change == ContentChangeKind.Removed
                    && !kept.Contains(documents[i].Id)
                )
                    plan.Add(new Step(ContentOperation.Delete, documents[i]));
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

    /// <summary>Runs a single step against the client, dispatched on its operation.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="step">The step to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write result.</returns>
    private static Task<UmbracoResponse<Empty>> Execute(
        IUmbracoManagementClient client,
        Step step,
        CancellationToken ct
    )
    {
        var change = step.Change;
        return step.Operation switch
        {
            // Create carries the document's own id (GUID-primary) already in the body; inject the
            // captured parent so it lands in the right place. The body is the normalised one the
            // diff compared (#224): the dates, flags and state are the source's, not the target's.
            ContentOperation.Create => client.CreateDocumentRawAsync(
                WithParent(change.DesiredBody!, change.Parent),
                ct
            ),
            ContentOperation.Update => client.UpdateDocumentRawAsync(
                change.Id,
                change.DesiredBody!,
                ct
            ),
            // A whole-document scope is a null culture list, which both endpoints take as "all".
            ContentOperation.Publish => client.PublishContentAsync(
                change.Id,
                step.Scope!.Cultures,
                ct: ct
            ),
            ContentOperation.Unpublish => client.UnpublishContentAsync(
                change.Id,
                step.Scope!.Cultures,
                ct
            ),
            ContentOperation.Delete => client.DeleteContentAsync(change.Id, ct),
            _ => throw new InvalidOperationException(
                $"{step.Operation} is not an executable apply operation."
            ),
        };
    }

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
