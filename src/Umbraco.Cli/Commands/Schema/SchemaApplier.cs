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
/// Order respects cross-kind dependencies: creates/updates run languages -> dictionary items ->
/// member groups -> data types -> templates -> media types -> member types -> document types ->
/// user groups (dictionary translations name languages, every type's properties reference data
/// types, a document type's <c>allowedTemplates</c> reference templates, and a user group's
/// property permissions reference document types). Within a kind, <b>creates</b> are
/// topologically ordered so a referenced same-kind entity (a composition, a template's master, a
/// dictionary item's parent, a language's fallback) is created before the entity that references
/// it. Deletes (prune) run in the reverse cross-kind order. Dictionary items and languages are
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
    /// A real (not dry-run) prune would delete a type still in use, a language, or a dictionary
    /// item with children the snapshot keeps, and <see cref="SchemaApplyOptions.Force"/> is false.
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
    /// (#252, #227, #269). The same checks the single deletes run, so a prune and a delete agree;
    /// dictionary items are checked against the plan instead, since a child the prune also deletes
    /// or moves away is expected to go.
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

        // A dictionary delete takes the item's children with it. A child is expected to go when
        // the prune deletes it too, or safe when this apply moves it to another parent first
        // (updates run before deletes). Any other child is one the snapshot keeps where it is,
        // and would be lost by surprise.
        var prunedDictionary = deletes
            .Where(o => o.Change.Kind == SchemaKinds.DictionaryItem)
            .Select(o => o.Change.CurrentId!.Value)
            .ToHashSet();
        var movedAway = plan.Where(o =>
                o.Operation == "update"
                && o.Change.Kind == SchemaKinds.DictionaryItem
                && SchemaBodies.ParentOf(o.Change.DesiredBody)
                    != SchemaBodies.ParentOf(o.Change.CurrentBody)
            )
            .Select(o => o.Change.CurrentId!.Value)
            .ToHashSet();
        // Null when the tree could not be read: an unknown is not a yes, so every pruned item
        // then needs --force.
        Dictionary<Guid, List<Guid>>? liveChildren = null;
        if (prunedDictionary.Count > 0)
        {
            var entries = await client.GetDictionaryEntriesAsync(ct);
            if (entries.IsSuccess)
            {
                liveChildren = [];
                foreach (var entry in entries.Data!)
                {
                    if (entry.ParentId is not { } parent)
                        continue;
                    if (!liveChildren.TryGetValue(parent, out var list))
                        liveChildren[parent] = list = [];
                    list.Add(entry.Id);
                }
            }
        }

        // A template is in use while a document type allows it or defaults to it, but not by a
        // document type this same prune deletes. The usage is read once for every template, and
        // null when it could not be read (an unknown is not a yes).
        var prunedDocumentTypes = deletes
            .Where(o => o.Change.Kind == SchemaKinds.DocumentType)
            .Select(o => o.Change.CurrentId!.Value)
            .ToHashSet();
        IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>? templateUsage = null;
        if (deletes.Any(o => o.Change.Kind == SchemaKinds.Template))
        {
            var usage = await client.GetTemplateUsageAsync(ct);
            if (usage.IsSuccess)
                templateUsage = usage.Data;
        }

        foreach (var op in deletes)
        {
            var change = op.Change;
            var reason = change.Kind switch
            {
                SchemaKinds.Language => InUseGuard.LanguageReason(change.Identity),
                SchemaKinds.Template => templateUsage is null
                    ? $"Could not read the document types to check whether template "
                        + $"'{change.Identity}' is in use."
                    : InUseGuard.TemplateReason(
                        change.CurrentId!.Value,
                        [
                            .. (
                                templateUsage.GetValueOrDefault(change.CurrentId!.Value) ?? []
                            ).Where(u => !prunedDocumentTypes.Contains(u.DocumentTypeId)),
                        ]
                    ),
                SchemaKinds.DictionaryItem => liveChildren is null
                    ? $"Could not read the dictionary tree to check whether '{change.Identity}' "
                        + "has children."
                    : KeptChildren(change, liveChildren, prunedDictionary, movedAway),
                // The same check the single deletes run, so a prune and a delete agree.
                _ => await InUseGuard.ReasonAsync(
                    client,
                    SchemaKinds.EntityOf(change.Kind),
                    change.CurrentId!.Value,
                    ct
                ),
            };
            if (reason is not null)
                blocked[op] = reason;
        }
        return blocked;
    }

    /// <summary>
    /// Why deleting a dictionary item would also delete children the snapshot keeps under it, or
    /// null when every child is pruned too, moved elsewhere by this apply, or there are none.
    /// </summary>
    /// <param name="change">The removed dictionary item.</param>
    /// <param name="liveChildren">Each live item's children.</param>
    /// <param name="pruned">Every dictionary item the prune deletes.</param>
    /// <param name="movedAway">Every dictionary item this apply moves to another parent.</param>
    /// <returns>The reason, or null.</returns>
    private static string? KeptChildren(
        SchemaEntityChange change,
        Dictionary<Guid, List<Guid>> liveChildren,
        HashSet<Guid> pruned,
        HashSet<Guid> movedAway
    )
    {
        if (!liveChildren.TryGetValue(change.CurrentId!.Value, out var children))
            return null;
        var kept = children.Count(c => !pruned.Contains(c) && !movedAway.Contains(c));
        return kept == 0
            ? null
            : $"Dictionary item '{change.Identity}' has {kept} child item(s) the snapshot keeps. "
                + "Deleting it also deletes them.";
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
    /// (see the class remarks; topologically sorted within each kind), then, if pruning, deletes
    /// in reverse.
    /// </summary>
    /// <param name="diff">The diff to plan.</param>
    /// <param name="prune">Whether to include deletes.</param>
    /// <returns>The ordered operations.</returns>
    private static List<Op> BuildPlan(SchemaDiff diff, bool prune)
    {
        var ops = new List<Op>();

        // Creates + updates, dependency order across kinds.
        foreach (
            var kind in new[]
            {
                diff.Languages,
                diff.DictionaryItems,
                diff.MemberGroups,
                diff.DataTypes,
                diff.Templates,
                diff.MediaTypes,
                diff.MemberTypes,
                diff.DocumentTypes,
                diff.UserGroups,
            }
        )
        {
            foreach (var added in CreateOrder(kind.Added))
                ops.Add(new Op("create", added));
            foreach (var changed in kind.Changed)
                ops.Add(new Op("update", changed));
        }

        // Deletes (prune) in reverse cross-kind order (user groups and doc types first, since
        // they depend on the rest). A Removed change carries the live body, but only languages
        // and dictionary items are ordered by it: their references are one known field. The
        // types delete in enumeration order and rely on fail-fast: Umbraco rejects a delete that
        // is still referenced, and a re-run (now that the referrer is gone) completes it.
        if (prune)
        {
            foreach (
                var kind in new[]
                {
                    diff.UserGroups,
                    diff.DocumentTypes,
                    diff.MemberTypes,
                    diff.MediaTypes,
                    diff.Templates,
                    diff.DataTypes,
                    diff.MemberGroups,
                    diff.DictionaryItems,
                    diff.Languages,
                }
            )
            {
                foreach (var removed in DeleteOrder(kind.Removed))
                    ops.Add(new Op("delete", removed));
            }
        }

        return ops;
    }

    /// <summary>
    /// Orders a kind's creates so a referenced entity is created first. Languages reference each
    /// other by ISO code (<c>fallbackIsoCode</c>); every other kind by an id somewhere in the body.
    /// </summary>
    /// <param name="added">One kind's added entities.</param>
    /// <returns>The ordered creates.</returns>
    private static List<SchemaEntityChange> CreateOrder(IReadOnlyList<SchemaEntityChange> added) =>
        added.FirstOrDefault()?.Kind == SchemaKinds.Language
            ? TopoOrder(added, c => c.Identity, c => Fallback(c.DesiredBody))
            : TopoOrder(
                added,
                c => c.DesiredId?.ToString(),
                c =>
                    c.DesiredBody is null
                        ? []
                        : ExtractGuids(c.DesiredBody).Select(g => g.ToString())
            );

    /// <summary>
    /// Orders a kind's deletes referrers-first, from the live bodies: a dictionary item before its
    /// parent (deleting the parent first would take the child and fail its delete), and a
    /// language before the language it falls back to. Other kinds keep enumeration order.
    /// </summary>
    /// <param name="removed">One kind's removed entities.</param>
    /// <returns>The ordered deletes.</returns>
    private static IEnumerable<SchemaEntityChange> DeleteOrder(
        IReadOnlyList<SchemaEntityChange> removed
    )
    {
        var kind = removed.FirstOrDefault()?.Kind;
        if (kind == SchemaKinds.DictionaryItem)
            return Enumerable.Reverse(
                TopoOrder(
                    removed,
                    c => c.CurrentId?.ToString(),
                    c =>
                        SchemaBodies.ParentOf(c.CurrentBody) is { } parent
                            ? [parent.ToString()]
                            : []
                )
            );
        if (kind == SchemaKinds.Language)
            return Enumerable.Reverse(
                TopoOrder(removed, c => c.Identity, c => Fallback(c.CurrentBody))
            );
        return removed;
    }

    /// <summary>A language body's fallback ISO code, as a reference list.</summary>
    /// <param name="body">The language body.</param>
    /// <returns>The fallback code, or nothing.</returns>
    private static IEnumerable<string> Fallback(JsonNode? body) =>
        body?["fallbackIsoCode"] is JsonValue v
        && v.TryGetValue<string>(out var iso)
        && !string.IsNullOrEmpty(iso)
            ? [iso]
            : [];

    /// <summary>
    /// Orders entities so that any entity referencing another entity in the batch comes *after*
    /// the entity it references. What an entity is called and what it references are supplied, so
    /// the one sort serves ids found anywhere in a body (compositions and master templates,
    /// without hard-coding the JSON path) and ISO codes alike. A reference cycle (which Umbraco
    /// itself disallows) is broken by falling back to input order for the entangled remainder
    /// rather than looping.
    /// </summary>
    /// <param name="changes">The changes to order.</param>
    /// <param name="selfKey">An entity's own key, or null when it has none.</param>
    /// <param name="references">The keys an entity references.</param>
    /// <returns>The dependency-ordered changes.</returns>
    private static List<SchemaEntityChange> TopoOrder(
        IReadOnlyList<SchemaEntityChange> changes,
        Func<SchemaEntityChange, string?> selfKey,
        Func<SchemaEntityChange, IEnumerable<string>> references
    )
    {
        if (changes.Count <= 1)
            return changes.ToList();

        // Work by index throughout: SchemaEntityChange is a value-equal record, so keying maps by
        // the change itself would throw on two equal entries — indices are always distinct.
        var inSet = changes.Select(selfKey).OfType<string>().ToHashSet();

        // deps[i] = the in-batch keys that changes[i] references (excluding its own key).
        var deps = new List<HashSet<string>>(changes.Count);
        foreach (var c in changes)
        {
            var self = selfKey(c);
            deps.Add(references(c).Where(k => inSet.Contains(k) && k != self).ToHashSet());
        }

        var ordered = new List<SchemaEntityChange>();
        var emitted = new HashSet<string>();
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
                if (selfKey(changes[i]) is { } key)
                    emitted.Add(key);
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
            // #227: languages are addressed by ISO code, dictionary items and user groups need
            // more than the one generic write.
            ("create", SchemaKinds.Language) => client.CreateLanguageRawAsync(
                change.DesiredBody!,
                ct
            ),
            ("update", SchemaKinds.Language) => client.UpdateLanguageRawAsync(
                change.Identity,
                change.DesiredBody!,
                ct
            ),
            ("delete", SchemaKinds.Language) => client.DeleteLanguageAsync(change.Identity, ct),
            ("update", SchemaKinds.DictionaryItem) => UpdateDictionaryItemAsync(client, change, ct),
            ("update", SchemaKinds.UserGroup) => UpdateUserGroupAsync(client, change, ct),

            // Creates and updates are the same call for every kind; only the endpoint differs.
            // An update is a full replace (replace: true): the snapshot body is the whole item.
            ("create", _) => client.CreateSchemaRawAsync(
                SchemaKinds.EntityOf(change.Kind),
                change.DesiredBody!,
                ct
            ),
            ("update", _) => client.MergeSchemaItemAsync(
                SchemaKinds.EntityOf(change.Kind),
                change.CurrentId!.Value,
                WithId(change.DesiredBody!, change.CurrentId!.Value),
                replace: true,
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
            ("delete", SchemaKinds.MediaType) => client.DeleteMediaTypeAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.MemberType) => client.DeleteMemberTypeAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.DictionaryItem) => client.DeleteDictionaryItemAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.MemberGroup) => client.DeleteMemberGroupAsync(
                change.CurrentId!.Value,
                ct
            ),
            ("delete", SchemaKinds.UserGroup) => client.DeleteUserGroupAsync(
                change.CurrentId!.Value,
                ct
            ),

            _ => throw new InvalidOperationException(
                $"Unknown schema operation {op.Operation}/{change.Kind}."
            ),
        };
    }

    /// <summary>
    /// Updates a dictionary item's name and translations, then moves it when its parent differs:
    /// the update model has no parent, so a re-parent is a separate call (#227).
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="change">The changed dictionary item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first failure, or an empty success.</returns>
    private static async Task<UmbracoResponse<Empty>> UpdateDictionaryItemAsync(
        IUmbracoManagementClient client,
        SchemaEntityChange change,
        CancellationToken ct
    )
    {
        var id = change.CurrentId!.Value;
        var update = await client.MergeSchemaItemAsync(
            EntityKind.DictionaryItem,
            id,
            WithId(SchemaBodies.WithoutParent(change.DesiredBody!), id),
            replace: true,
            ct
        );
        if (!update.IsSuccess)
            return update;

        var parent = SchemaBodies.ParentOf(change.DesiredBody);
        return parent == SchemaBodies.ParentOf(change.CurrentBody)
            ? update
            : await client.MoveDictionaryItemAsync(id, parent, ct);
    }

    /// <summary>
    /// Updates a user group with the snapshot body plus the target's own start nodes and
    /// per-document permissions, which the snapshot leaves out (#227). The live group is read in
    /// full here, because the diff's live body has those parts removed too.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="change">The changed user group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first failure, or an empty success.</returns>
    private static async Task<UmbracoResponse<Empty>> UpdateUserGroupAsync(
        IUmbracoManagementClient client,
        SchemaEntityChange change,
        CancellationToken ct
    )
    {
        var id = change.CurrentId!.Value;
        var live = await client.GetSchemaRawAsync(EntityKind.UserGroup, id, ct);
        if (!live.IsSuccess)
            return UmbracoResponse<Empty>.Failure(live.StatusCode, live.ErrorMessage!);
        return await client.MergeSchemaItemAsync(
            EntityKind.UserGroup,
            id,
            WithId(SchemaBodies.WithLiveNodes(change.DesiredBody!, live.Data!), id),
            replace: true,
            ct
        );
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
