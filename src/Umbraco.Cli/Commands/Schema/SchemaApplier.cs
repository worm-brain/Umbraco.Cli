using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// One create/update/delete step in an apply run (issue #68), as reported to the user. For a
/// dry run <see cref="Status"/> is <c>planned</c>; for a real run it is <c>success</c> (the
/// executed steps up to any failure) — a failed run surfaces its error through the command's
/// exit code and message rather than a per-step <c>error</c> row, since apply is fail-fast.
/// </summary>
/// <param name="Operation">The step: <c>create</c>, <c>update</c>, or <c>delete</c>.</param>
/// <param name="Kind">The entity kind: <c>documentType</c>, <c>mediaType</c>, <c>memberType</c>, <c>dataType</c>, or <c>template</c>.</param>
/// <param name="Identity">The entity's human identity (alias/name).</param>
/// <param name="Id">The entity id the step targets (the live id for update/delete; the new id for create).</param>
/// <param name="Status">The step status: <c>planned</c> (dry run) or <c>success</c> (executed).</param>
public sealed record SchemaAction(
    string Operation,
    string Kind,
    string Identity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? Id,
    string Status
);

/// <summary>
/// The outcome of an apply run (issue #68): whether it was a dry run, whether prune was on, and
/// the ordered steps (planned or executed). Counts are derived for a quick summary.
/// </summary>
/// <param name="DryRun">True when nothing was written (a plan preview).</param>
/// <param name="Pruned">True when <c>--prune</c> was in effect (deletes were part of the plan).</param>
/// <param name="Actions">The ordered create/update/delete steps.</param>
public sealed record SchemaApplyResult(
    bool DryRun,
    bool Pruned,
    IReadOnlyList<SchemaAction> Actions
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
/// Executes (or, under dry run, plans) the changes a <see cref="SchemaDiff"/> describes
/// (issue #68 / ADR 0005 §4). It never computes a diff itself — the command feeds it one — so
/// the ordering/execution logic is testable in isolation.
///
/// Order respects cross-kind dependencies: creates/updates run static files first (#292: folders
/// shallowest first, then files, so no template renders against a missing partial), then languages -> dictionary items ->
/// member groups -> data types -> templates -> media types -> member types -> document types ->
/// user groups (dictionary translations name languages, every type's properties reference data
/// types, a document type's <c>allowedTemplates</c> reference templates, and a user group's
/// property permissions reference document types). Within a kind, <b>creates</b> are
/// topologically ordered so a referenced same-kind entity (a composition, a template's master, a
/// dictionary item's parent, a language's fallback) is created before the entity that references
/// it. Deletes (prune) run in the reverse cross-kind order, static files last (files, then
/// folders deepest first). Dictionary items and languages are
/// deleted referrers-first (children before parents, a language before its fallback); the other
/// kinds in enumeration order — Umbraco rejects a delete that is still depended on, which
/// fail-fast surfaces and a re-run resolves.
/// Apply is **fail-fast**: the first failed write stops the run so a broken state is not piled
/// onto.
/// </summary>
public static class SchemaApplier
{
    /// <summary>
    /// Applies <paramref name="diff"/> to the live instance, or (under
    /// <see cref="SchemaApplyOptions.DryRun"/>) returns the plan without writing anything.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff to apply (already computed against the live instance).</param>
    /// <param name="options">Whether to prune, whether to write, and whether to force in-use prunes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The apply result, or the first write failure.</returns>
    /// <exception cref="SafetyRefusalException">
    /// A real (not dry-run) prune would delete a type still in use, a language, a dictionary
    /// item with children the snapshot keeps, or a static file a template names (#292), and
    /// <see cref="SchemaApplyOptions.Force"/> is false.
    /// Nothing has been applied.
    /// </exception>
    public static async Task<UmbracoResponse<SchemaApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        SchemaDiff diff,
        SchemaApplyOptions options,
        CancellationToken ct
    )
    {
        var (prune, dryRun, force) = options;
        var plan = BuildPlan(diff, prune);

        // #252: a pruned type that is still in use takes content with it (Umbraco cascades the
        // delete). Check every planned delete before the first write, so a refusal applies
        // nothing at all rather than stopping halfway.
        var blocked = force ? [] : await InUseDeletesAsync(client, plan, ct);

        if (dryRun)
        {
            // Preview only: report every step as "planned" and touch nothing. An in-use delete is
            // marked so the preview shows what a real run would refuse.
            var planned = plan.Select(op =>
                    op.ToAction(blocked.ContainsKey(op) ? "needs --force" : "planned")
                )
                .ToList();
            return UmbracoResponse<SchemaApplyResult>.Success(
                new SchemaApplyResult(DryRun: true, Pruned: prune, planned)
            );
        }

        if (blocked.Count > 0)
            throw new SafetyRefusalException(
                $"Refusing to prune {blocked.Count} item(s) whose delete loses more than the item: "
                    + string.Join(" ", blocked.Values)
                    + $" Nothing was applied. Re-run with {InUseGuard.ForceOption} to prune them anyway."
            );

        // Execute in order, stopping at the first failure (fail-fast) so a dependency error does
        // not cascade. Steps already done are reported in the failure message.
        var done = new List<SchemaAction>();
        foreach (var op in plan)
        {
            var result = await Execute(client, op, ct);
            if (!result.IsSuccess)
            {
                var doneSummary =
                    done.Count == 0 ? "no changes were applied" : $"{done.Count} change(s) applied";
                return UmbracoResponse<SchemaApplyResult>.Failure(
                    result.StatusCode,
                    $"Apply failed on {op.Operation} {op.Change.Kind} '{op.Change.Identity}' "
                        + $"({doneSummary} before the failure): {result.ErrorMessage}"
                );
            }
            done.Add(op.ToAction("success"));
        }

        return UmbracoResponse<SchemaApplyResult>.Success(
            new SchemaApplyResult(DryRun: false, Pruned: prune, done)
        );
    }

    /// <summary>
    /// The planned deletes that would take more than the item with them, each with the reason
    /// (#252, #227, #269). Every delete goes through the one check the single deletes run,
    /// <see cref="InUseGuard.ReasonAsync"/>, given the plan (#281): what the same prune also
    /// deletes or moves away is expected to go, and a static file is checked against the templates
    /// as this apply leaves them.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="plan">The ordered plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blocked operations and why, in plan order.</returns>
    private static async Task<Dictionary<Op, string>> InUseDeletesAsync(
        IUmbracoManagementClient client,
        IReadOnlyList<Op> plan,
        CancellationToken ct
    )
    {
        var blocked = new Dictionary<Op, string>();
        var deletes = plan.Where(o => o.Operation == "delete").ToList();
        if (deletes.Count == 0)
            return blocked;

        var context = PlanContext(plan);
        foreach (var op in deletes)
        {
            var target = SchemaKinds.Of(op.Change.Kind).Target(op.Change);
            if (await InUseGuard.ReasonAsync(client, target, context, ct) is { } reason)
                blocked[op] = reason;
        }
        return blocked;
    }

    /// <summary>
    /// What the delete checks need to know about the rest of the plan (#281): every id-keyed item
    /// it deletes, every dictionary item it moves to another parent (updates run before deletes,
    /// so a moved child is safe), and every template it writes (so a file check sees the templates
    /// as they will stand).
    /// </summary>
    /// <param name="plan">The ordered plan.</param>
    /// <returns>The plan context.</returns>
    private static DeletePlanContext PlanContext(IReadOnlyList<Op> plan)
    {
        var deleted = plan.Where(o => o.Operation == "delete")
            .Select(o => (Kind: SchemaKinds.Of(o.Change.Kind).Entity, Id: o.Change.CurrentId))
            .Where(d => d.Kind is not null && d.Id is not null)
            .Select(d => (d.Kind!.Value, d.Id!.Value));
        var movedAway = plan.Where(o =>
                o.Operation == "update"
                && o.Change.Kind == SchemaKinds.DictionaryItem
                && SchemaBodies.ParentOf(o.Change.DesiredBody)
                    != SchemaBodies.ParentOf(o.Change.CurrentBody)
            )
            .Select(o => o.Change.CurrentId!.Value);
        var templates = plan.Where(o => o.Change.Kind == SchemaKinds.Template).ToList();
        return new DeletePlanContext(
            deleted,
            movedAway,
            templates
                .Where(o => o.Operation == "update")
                .ToDictionary(o => o.Change.CurrentId!.Value, o => o.Change.DesiredBody!),
            [.. templates.Where(o => o.Operation == "create").Select(o => o.Change.DesiredBody!)]
        );
    }

    /// <summary>A single planned operation: the change plus which verb to run for it.</summary>
    /// <param name="Operation">create / update / delete.</param>
    /// <param name="Change">The diff entry to act on.</param>
    private sealed record Op(string Operation, SchemaEntityChange Change)
    {
        /// <summary>Projects this planned op into a user-facing <see cref="SchemaAction"/>.</summary>
        /// <param name="status">The status to stamp (<c>planned</c> or <c>success</c>).</param>
        /// <returns>The action record.</returns>
        public SchemaAction ToAction(string status) =>
            new(
                Operation,
                Change.Kind,
                Change.Identity,
                // Create targets the new (snapshot) id; update/delete target the live id.
                Operation == "create"
                    ? Change.DesiredId
                    : Change.CurrentId,
                status
            );
    }

    /// <summary>
    /// Builds the ordered operation list from a diff, from the kind table (#273): each kind's
    /// creates (in its own create order) and updates, kinds in <see cref="SchemaKinds.CreateOrder"/>;
    /// then, if pruning, each kind's deletes (in its own delete order), kinds in
    /// <see cref="SchemaKinds.DeleteOrder"/>.
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="prune">Whether to include deletes.</param>
    /// <returns>The ordered operations.</returns>
    private static List<Op> BuildPlan(SchemaDiff diff, bool prune)
    {
        var ops = new List<Op>();
        foreach (var kind in SchemaKinds.CreateOrder)
        {
            var kindDiff = kind.Diff(diff);
            ops.AddRange(kind.CreateOrder(kindDiff.Added).Select(c => new Op("create", c)));
            ops.AddRange(kindDiff.Changed.Select(c => new Op("update", c)));
        }

        // Deletes (prune). A Removed change carries the live body, but only languages, dictionary
        // items and files are ordered by it: their references are one known field. The types
        // delete in enumeration order and rely on fail-fast: Umbraco rejects a delete that is
        // still referenced, and a re-run (now that the referrer is gone) completes it.
        if (prune)
            foreach (var kind in SchemaKinds.DeleteOrder)
                ops.AddRange(
                    kind.DeleteOrder(kind.Diff(diff).Removed).Select(c => new Op("delete", c))
                );

        return ops;
    }

    /// <summary>Runs a single planned operation through its kind's write adapter.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="op">The operation to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write result.</returns>
    /// <exception cref="InvalidOperationException">The operation is not create, update or delete.</exception>
    private static Task<UmbracoResponse<Empty>> Execute(
        IUmbracoManagementClient client,
        Op op,
        CancellationToken ct
    )
    {
        var kind = SchemaKinds.Of(op.Change.Kind);
        var write = op.Operation switch
        {
            "create" => kind.Create,
            "update" => kind.Update,
            "delete" => kind.Delete,
            _ => throw new InvalidOperationException(
                $"Unknown schema operation {op.Operation}/{op.Change.Kind}."
            ),
        };
        return write(client, op.Change, ct);
    }
}
