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

        // #292: a pruned static file breaks a template that names it. The templates are read once,
        // as they will stand after this apply; null when they could not be read (an unknown is
        // not a yes, so every pruned file then needs --force).
        IReadOnlyList<(string Name, string Content)>? templates = null;
        if (
            deletes.Any(o =>
                SchemaKinds.Of(o.Change.Kind).File is not null
                && !SchemaStaticFiles.IsFolder(o.Change.CurrentBody)
            )
        )
            templates = await TemplatesAfterApplyAsync(client, plan, ct);

        foreach (var op in deletes)
        {
            var change = op.Change;
            if (SchemaKinds.Of(change.Kind).File is { } fileKind)
            {
                // A folder holds nothing the snapshot keeps (the diff implies those folders), so
                // only files are checked.
                if (
                    !SchemaStaticFiles.IsFolder(change.CurrentBody)
                    && FileReason(fileKind, change.Identity, templates) is { } fileReason
                )
                    blocked[op] = fileReason;
                continue;
            }

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
                    SchemaKinds.Of(change.Kind).Entity!.Value,
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
    /// Why pruning a static file would break a template (#292), or null when no template names
    /// it. A template "names" a file when its content contains the file name, or, for a partial
    /// view, its path without the extension in quotes (<c>Html.PartialAsync("header")</c>); see
    /// <see cref="SchemaStaticFiles.SearchTerms"/>. A text search can miss a reference built at
    /// run time, and can match a longer name, but it errs towards asking for <c>--force</c>.
    /// </summary>
    /// <param name="kind">The file's kind.</param>
    /// <param name="path">The file path.</param>
    /// <param name="templates">The templates after the apply, or null when they could not be read.</param>
    /// <returns>The reason, or null.</returns>
    internal static string? FileReason(
        StaticFileKind kind,
        string path,
        IReadOnlyList<(string Name, string Content)>? templates
    )
    {
        if (templates is null)
            return $"Could not read the templates to check whether '{path}' is in use.";
        var users = templates
            .Where(t => SchemaStaticFiles.Mentions(t.Content, kind, path))
            .Select(t => $"'{t.Name}'")
            .ToList();
        return users.Count == 0
            ? null
            : $"'{path}' is named by template(s) {string.Join(", ", users)}; deleting it breaks them.";
    }

    /// <summary>
    /// Every template's name and content as they will stand after this apply (#292): the live
    /// templates, less the ones the plan deletes, with the snapshot content for the ones it
    /// updates, plus the ones it creates. So a template the same apply rewrites to stop using a
    /// file does not block that file's prune, and one it adds does.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="plan">The ordered plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The templates, or null when the live ones could not be read.</returns>
    private static async Task<IReadOnlyList<(
        string Name,
        string Content
    )>?> TemplatesAfterApplyAsync(
        IUmbracoManagementClient client,
        IReadOnlyList<Op> plan,
        CancellationToken ct
    )
    {
        var ids = await client.GetTemplateIdsAsync(ct);
        if (!ids.IsSuccess)
            return null;

        var ops = plan.Where(o => o.Change.Kind == SchemaKinds.Template).ToList();
        var deleted = ops.Where(o => o.Operation == "delete")
            .Select(o => o.Change.CurrentId)
            .ToHashSet();
        var updated = ops.Where(o => o.Operation == "update")
            .ToDictionary(o => o.Change.CurrentId!.Value, o => o.Change.DesiredBody!);

        var result = new List<(string, string)>();
        foreach (var id in ids.Data!.Where(i => !deleted.Contains(i)))
        {
            JsonNode body;
            if (updated.TryGetValue(id, out var desired))
                body = desired;
            else
            {
                var live = await client.GetSchemaRawAsync(EntityKind.Template, id, ct);
                if (!live.IsSuccess)
                    return null;
                body = live.Data!;
            }
            result.Add(TemplateText(body));
        }
        result.AddRange(
            ops.Where(o => o.Operation == "create").Select(o => TemplateText(o.Change.DesiredBody!))
        );
        return result;
    }

    /// <summary>A template body's display name (its name, else alias) and its Razor content.</summary>
    /// <param name="body">The template body.</param>
    /// <returns>The name and content.</returns>
    private static (string Name, string Content) TemplateText(JsonNode body) =>
        ((string?)body["name"] ?? (string?)body["alias"] ?? "", (string?)body["content"] ?? "");

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
