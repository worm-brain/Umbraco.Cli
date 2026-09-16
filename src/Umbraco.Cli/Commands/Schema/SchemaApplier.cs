using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// One create/update/delete step in an apply run (issue #68), as reported to the user. For a
/// dry run <see cref="Status"/> is <c>planned</c>; for a real run it is <c>success</c> (the
/// executed steps up to any failure) — a failed run surfaces its error through the command's
/// exit code and message rather than a per-step <c>error</c> row, since apply is fail-fast.
/// </summary>
/// <param name="Operation">The step: <c>create</c>, <c>update</c>, or <c>delete</c>.</param>
/// <param name="Kind">The entity kind: <c>documentType</c>, <c>dataType</c>, or <c>template</c>.</param>
/// <param name="Identity">The entity's human identity (alias/name).</param>
/// <param name="Id">The entity id the step targets (the live id for update/delete; the new id for create).</param>
/// <param name="Status">The step status: <c>planned</c> (dry run) or <c>success</c> (executed).</param>
public sealed record SchemaAction(
    string Operation,
    string Kind,
    string Identity,
    Guid? Id,
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
/// Order respects cross-kind dependencies: creates/updates run data types -> templates ->
/// document types (a document type's properties reference data types and its
/// <c>allowedTemplates</c> reference templates). Within a kind, <b>creates</b> are topologically
/// ordered so a referenced same-kind entity (a composition, or a template's master) is created
/// before the entity that references it. Deletes (prune) run in the reverse cross-kind order
/// (document types -> templates -> data types) in each kind's enumeration order — Umbraco
/// rejects a delete that is still depended on, which fail-fast surfaces and a re-run resolves.
/// Apply is **fail-fast**: the first failed write stops the run so a broken state is not piled
/// onto.
/// </summary>
public static class SchemaApplier
{
    /// <summary>
    /// Applies <paramref name="diff"/> to the live instance, or (when <paramref name="dryRun"/>)
    /// returns the plan without writing anything.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="diff">The diff to apply (already computed against the live instance).</param>
    /// <param name="prune">When true, delete live entities the snapshot matched nothing to.</param>
    /// <param name="dryRun">When true, compute the plan and write nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The apply result, or the first write failure.</returns>
    public static async Task<UmbracoResponse<SchemaApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        SchemaDiff diff,
        bool prune,
        bool dryRun,
        CancellationToken ct
    )
    {
        var plan = BuildPlan(diff, prune);

        if (dryRun)
        {
            // Preview only: report every step as "planned" and touch nothing.
            var planned = plan.Select(op => op.ToAction("planned")).ToList();
            return UmbracoResponse<SchemaApplyResult>.Success(
                new SchemaApplyResult(DryRun: true, Pruned: prune, planned)
            );
        }

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
    /// Builds the ordered operation list from a diff: creates/updates in dependency order
    /// (data types -> templates -> document types, topologically sorted within each), then, if
    /// pruning, deletes in reverse.
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="prune">Whether to include deletes.</param>
    /// <returns>The ordered operations.</returns>
    private static List<Op> BuildPlan(SchemaDiff diff, bool prune)
    {
        var ops = new List<Op>();

        // Creates + updates, dependency order across kinds.
        foreach (var kind in new[] { diff.DataTypes, diff.Templates, diff.DocumentTypes })
        {
            foreach (var added in TopoOrder(kind.Added))
                ops.Add(new Op("create", added));
            foreach (var changed in kind.Changed)
                ops.Add(new Op("update", changed));
        }

        // Deletes (prune) in reverse cross-kind order (doc types first, since they depend on
        // templates and data types). Within a kind we cannot topologically order deletes — a
        // Removed change carries no body (nothing to inspect for references) — so we delete in
        // enumeration order and rely on fail-fast: Umbraco rejects a delete that is still
        // referenced, and a re-run (now that the referrer is gone) completes it.
        if (prune)
        {
            foreach (var kind in new[] { diff.DocumentTypes, diff.Templates, diff.DataTypes })
            {
                foreach (var removed in kind.Removed)
                    ops.Add(new Op("delete", removed));
            }
        }

        return ops;
    }

    /// <summary>
    /// Orders entities so that any entity referencing another entity of the same kind (by its id
    /// appearing anywhere in the referrer's body — captures compositions and master templates
    /// generically, without hard-coding the JSON path) comes *after* the entity it references.
    /// A reference cycle (which Umbraco itself disallows) is broken by falling back to input
    /// order for the entangled remainder rather than looping.
    /// </summary>
    /// <param name="changes">The changes to order (their <see cref="SchemaEntityChange.DesiredBody"/> is scanned).</param>
    /// <returns>The dependency-ordered changes.</returns>
    private static List<SchemaEntityChange> TopoOrder(IReadOnlyList<SchemaEntityChange> changes)
    {
        if (changes.Count <= 1)
            return changes.ToList();

        // Work by index throughout: SchemaEntityChange is a value-equal record, so keying maps by
        // the change itself would throw on two equal entries — indices are always distinct.
        var inSet = changes
            .Where(c => c.DesiredId is not null)
            .Select(c => c.DesiredId!.Value)
            .ToHashSet();

        // deps[i] = the in-batch ids that changes[i] references (excluding its own id).
        var deps = new List<HashSet<Guid>>(changes.Count);
        foreach (var c in changes)
        {
            var self = c.DesiredId;
            deps.Add(
                c.DesiredBody is null
                    ? []
                    : ExtractGuids(c.DesiredBody)
                        .Where(g => inSet.Contains(g) && g != self)
                        .ToHashSet()
            );
        }

        var ordered = new List<SchemaEntityChange>();
        var emitted = new HashSet<Guid>();
        var remaining = Enumerable.Range(0, changes.Count).ToList();

        while (remaining.Count > 0)
        {
            // Emit every entity whose in-batch dependencies are all already emitted.
            var ready = remaining.Where(i => deps[i].All(emitted.Contains)).ToList();

            if (ready.Count == 0)
            {
                // Cycle (or a self-referential remainder): emit the rest in input order so we make
                // progress instead of spinning. Umbraco will reject a genuine impossible order.
                ordered.AddRange(remaining.Select(i => changes[i]));
                break;
            }

            foreach (var i in ready)
            {
                ordered.Add(changes[i]);
                if (changes[i].DesiredId is { } id)
                    emitted.Add(id);
                remaining.Remove(i);
            }
        }

        return ordered;
    }

    /// <summary>Recursively collects every GUID-valued string in a JSON body.</summary>
    /// <param name="node">The node to scan.</param>
    /// <returns>Every parseable GUID found, with duplicates.</returns>
    private static IEnumerable<Guid> ExtractGuids(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    if (value is null)
                        continue;
                    foreach (var g in ExtractGuids(value))
                        yield return g;
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is null)
                        continue;
                    foreach (var g in ExtractGuids(item))
                        yield return g;
                }
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
                    yield return guid;
                break;
        }
    }

    /// <summary>
    /// Runs a single planned operation against the client. For an update the request body's
    /// <c>id</c> is rewritten to the live target id before sending, so an alias-matched update
    /// whose snapshot id differs (see <see cref="SchemaEntityChange.IdMismatch"/>) targets the
    /// existing entity rather than trying to change its immutable id.
    /// </summary>
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
        return (op.Operation, change.Kind) switch
        {
            ("create", SchemaKinds.DocumentType) => client.CreateDocumentTypeRawAsync(
                change.DesiredBody!,
                ct
            ),
            ("create", SchemaKinds.DataType) => client.CreateDataTypeRawAsync(
                change.DesiredBody!,
                ct
            ),
            ("create", SchemaKinds.Template) => client.CreateTemplateRawAsync(
                change.DesiredBody!,
                ct
            ),

            ("update", SchemaKinds.DocumentType) => client.UpdateDocumentTypeRawAsync(
                change.CurrentId!.Value,
                WithId(change.DesiredBody!, change.CurrentId!.Value),
                ct
            ),
            ("update", SchemaKinds.DataType) => client.UpdateDataTypeRawAsync(
                change.CurrentId!.Value,
                WithId(change.DesiredBody!, change.CurrentId!.Value),
                ct
            ),
            ("update", SchemaKinds.Template) => client.UpdateTemplateRawAsync(
                change.CurrentId!.Value,
                WithId(change.DesiredBody!, change.CurrentId!.Value),
                ct
            ),

            ("delete", SchemaKinds.DocumentType) => client.DeleteDocumentTypeAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.DataType) => client.DeleteDataTypeAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.Template) => client.DeleteTemplateAsync(
                change.CurrentId!.Value,
                ct
            ),

            _ => throw new InvalidOperationException(
                $"Unknown schema operation {op.Operation}/{change.Kind}."
            ),
        };
    }

    /// <summary>
    /// Returns a clone of <paramref name="body"/> with its top-level <c>id</c> set to
    /// <paramref name="id"/>, so an update targets the live entity's id. The original node is not
    /// mutated (it may be shared with the diff output).
    /// </summary>
    /// <param name="body">The snapshot body.</param>
    /// <param name="id">The live id to stamp.</param>
    /// <returns>A cloned body carrying the live id.</returns>
    private static JsonNode WithId(JsonNode body, Guid id)
    {
        var clone = body.DeepClone();
        clone["id"] = id.ToString();
        return clone;
    }
}
