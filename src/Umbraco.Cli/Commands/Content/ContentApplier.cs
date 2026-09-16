using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// One create/update/delete step in a content apply run (issue #100), as reported to the user. For
/// a dry run <see cref="Status"/> is <c>planned</c>; for a real run it is <c>success</c> (the
/// executed steps up to any failure) - apply is fail-fast, so a failure surfaces through the exit
/// code and message rather than a per-step error row.
/// </summary>
/// <param name="Operation">The step: <c>create</c>, <c>update</c>, or <c>delete</c>.</param>
/// <param name="Id">The document id the step targets.</param>
/// <param name="Status">The step status: <c>planned</c> (dry run) or <c>success</c> (executed).</param>
public sealed record ContentAction(string Operation, Guid Id, string Status);

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
    public int Created => Actions.Count(a => a.Operation == "create");

    /// <summary>Number of update steps.</summary>
    public int Updated => Actions.Count(a => a.Operation == "update");

    /// <summary>Number of delete steps.</summary>
    public int Deleted => Actions.Count(a => a.Operation == "delete");
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
/// existing documents (a placement drift is reported by <c>diff</c> but a move is out of scope).
/// </summary>
public static class ContentApplier
{
    /// <summary>
    /// Applies <paramref name="diff"/> to the live instance, or (when <paramref name="dryRun"/>)
    /// returns the plan without writing anything.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff to apply (already computed against the live instance).</param>
    /// <param name="prune">When true, delete live documents the snapshot matched nothing to.</param>
    /// <param name="dryRun">When true, compute the plan and write nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The apply result, or the first write failure.</returns>
    public static async Task<UmbracoResponse<ContentApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        ContentDiff diff,
        bool prune,
        bool dryRun,
        CancellationToken ct
    )
    {
        var plan = BuildPlan(diff, prune);

        if (dryRun)
        {
            var planned = plan.Select(op => op.ToAction("planned")).ToList();
            return UmbracoResponse<ContentApplyResult>.Success(
                new ContentApplyResult(DryRun: true, Pruned: prune, planned)
            );
        }

        var done = new List<ContentAction>();
        foreach (var op in plan)
        {
            var result = await Execute(client, op, ct);
            if (!result.IsSuccess)
            {
                var doneSummary =
                    done.Count == 0 ? "no changes were applied" : $"{done.Count} change(s) applied";
                return UmbracoResponse<ContentApplyResult>.Failure(
                    result.StatusCode,
                    $"Apply failed on {op.Operation} document '{op.Change.Id}' "
                        + $"({doneSummary} before the failure): {result.ErrorMessage}"
                );
            }
            done.Add(op.ToAction("success"));
        }

        return UmbracoResponse<ContentApplyResult>.Success(
            new ContentApplyResult(DryRun: false, Pruned: prune, done)
        );
    }

    /// <summary>A single planned operation: the change plus which verb to run for it.</summary>
    /// <param name="Operation">create / update / delete.</param>
    /// <param name="Change">The diff entry to act on.</param>
    private sealed record Op(string Operation, ContentDocumentChange Change)
    {
        /// <summary>Projects this planned op into a user-facing <see cref="ContentAction"/>.</summary>
        /// <param name="status">The status to stamp (<c>planned</c> or <c>success</c>).</param>
        /// <returns>The action record.</returns>
        public ContentAction ToAction(string status) => new(Operation, Change.Id, status);
    }

    /// <summary>
    /// Builds the ordered operation list from a diff: creates in snapshot pre-order (parents
    /// first), then updates, then - if pruning - deletes in reverse (deepest documents first, so a
    /// parent is never deleted while it still has children).
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="prune">Whether to include deletes.</param>
    /// <returns>The ordered operations.</returns>
    private static List<Op> BuildPlan(ContentDiff diff, bool prune)
    {
        var ops = new List<Op>();

        // Creates preserve the snapshot's pre-order (parent before child); nothing to topo-sort.
        foreach (var added in diff.Added)
            ops.Add(new Op("create", added));

        foreach (var changed in diff.Changed)
            ops.Add(new Op("update", changed));

        // Deletes in reverse tree order: the Removed list is in live pre-order (parents first), so
        // reversing it deletes children before their parents. Fail-fast covers any residual order
        // issue (a re-run completes once the blocker is gone).
        if (prune)
        {
            for (var i = diff.Removed.Count - 1; i >= 0; i--)
                ops.Add(new Op("delete", diff.Removed[i]));
        }

        return ops;
    }

    /// <summary>Runs a single planned operation against the client.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="op">The operation to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write result.</returns>
    private static Task<UmbracoResponse<Empty>> Execute(
        IUmbracoManagementClient client,
        Op op,
        CancellationToken ct
    )
    {
        var change = op.Change;
        return op.Operation switch
        {
            // Create carries the document's own id (GUID-primary) already in the body; inject the
            // captured parent so it lands in the right place.
            "create" => client.CreateDocumentRawAsync(
                WithParent(change.DesiredBody!, change.Parent),
                ct
            ),
            "update" => client.UpdateDocumentRawAsync(change.Id, change.DesiredBody!, ct),
            "delete" => client.DeleteContentAsync(change.Id, ct),
            _ => throw new InvalidOperationException($"Unknown content operation {op.Operation}."),
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
