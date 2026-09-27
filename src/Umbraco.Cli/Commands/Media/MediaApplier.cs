using System.Text.Json.Serialization;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Commands.Media;

/// <summary>A media apply step (#226). Serialized in lower case, like the content steps.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaOperation>))]
public enum MediaOperation
{
    /// <summary>Create an item with its snapshot id and parent, uploading its file.</summary>
    [JsonStringEnumMemberName("create")]
    Create,

    /// <summary>Replace an item's body, uploading its file when the file differs.</summary>
    [JsonStringEnumMemberName("update")]
    Update,

    /// <summary>Move an item the snapshot omits to the recycle bin (prune).</summary>
    [JsonStringEnumMemberName("trash")]
    Trash,
}

/// <summary>One step of a media apply run, as reported.</summary>
/// <param name="Operation">The step.</param>
/// <param name="Id">The item id.</param>
/// <param name="File">The file name the step uploads, or null when it uploads none.</param>
/// <param name="Status"><c>planned</c> (dry run) or <c>success</c>.</param>
public sealed record MediaAction(
    MediaOperation Operation,
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? File,
    string Status
);

/// <summary>The outcome of a media apply run.</summary>
/// <param name="DryRun">True when nothing was written.</param>
/// <param name="Pruned">True when <c>--prune</c> was in effect.</param>
/// <param name="Actions">The ordered steps.</param>
public sealed record MediaApplyResult(bool DryRun, bool Pruned, IReadOnlyList<MediaAction> Actions);

/// <summary>
/// Executes (or, under a dry run, plans) a <see cref="MediaDiff"/> (#226, ADR 0008).
/// <list type="bullet">
/// <item><b>Creates</b> run in snapshot pre-order, so a folder exists before what is in it. Each
/// carries the snapshot id and parent; an item with a file stages the file first and points
/// <c>umbracoFile</c> at it.</item>
/// <item><b>Updates</b> replace the body. A changed file is staged and set; otherwise the live
/// file is kept, since a PUT without it would drop it.</item>
/// <item><b>Prune</b> moves items to the recycle bin rather than deleting them, deepest first:
/// content references media by id, and a trashed item can be restored. An item is left alone when
/// something the snapshot keeps is under it, because trashing it would take that along.</item>
/// </list>
/// Apply is fail-fast, and never moves an item (placement drift is reported by diff).
/// </summary>
public static class MediaApplier
{
    /// <summary>One planned step.</summary>
    /// <param name="Operation">The step.</param>
    /// <param name="Change">The diff entry it came from.</param>
    private sealed record Step(MediaOperation Operation, MediaItemChange Change)
    {
        /// <summary>The step as reported.</summary>
        /// <param name="status">The status.</param>
        /// <returns>The action row.</returns>
        public MediaAction ToAction(string status) =>
            new(
                Operation,
                Change.Id,
                Operation != MediaOperation.Trash && Change.FileChanged ? Change.File?.Name : null,
                status
            );
    }

    /// <summary>Applies <paramref name="diff"/>, or returns the plan under a dry run.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="snapshot">The snapshot the diff came from (its files are uploaded).</param>
    /// <param name="diff">The diff.</param>
    /// <param name="prune">Also trash live items the snapshot omits.</param>
    /// <param name="dryRun">Plan only; write nothing and stage nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result, or the first failure.</returns>
    public static async Task<UmbracoResponse<MediaApplyResult>> ApplyAsync(
        IUmbracoManagementClient client,
        MediaSnapshot snapshot,
        MediaDiff diff,
        bool prune,
        bool dryRun,
        CancellationToken ct
    )
    {
        var plan = BuildPlan(diff, prune);
        if (dryRun)
            return UmbracoResponse<MediaApplyResult>.Success(
                new MediaApplyResult(true, prune, [.. plan.Select(s => s.ToAction("planned"))])
            );

        var done = new List<MediaAction>();
        foreach (var step in plan)
        {
            var result = await ExecuteAsync(client, snapshot, step, ct);
            if (!result.IsSuccess)
            {
                var doneSummary =
                    done.Count == 0 ? "no changes were applied" : $"{done.Count} change(s) applied";
                return UmbracoResponse<MediaApplyResult>.Failure(
                    result.StatusCode,
                    $"Apply failed on {step.Operation.ToString().ToLowerInvariant()} media item "
                        + $"'{step.Change.Id}' ({doneSummary} before the failure): {result.ErrorMessage}"
                );
            }
            done.Add(step.ToAction("success"));
        }
        return UmbracoResponse<MediaApplyResult>.Success(new MediaApplyResult(false, prune, done));
    }

    /// <summary>Creates, then updates, then (pruning) trashes deepest first.</summary>
    /// <param name="diff">The diff.</param>
    /// <param name="prune">Whether to trash removed items.</param>
    /// <returns>The ordered steps.</returns>
    private static List<Step> BuildPlan(MediaDiff diff, bool prune)
    {
        var items = diff.Items;
        var plan = new List<Step>();
        plan.AddRange(
            items
                .Where(i => i.Change == ContentChangeKind.Added)
                .Select(i => new Step(MediaOperation.Create, i))
        );
        plan.AddRange(
            items
                .Where(i => i.Change == ContentChangeKind.Changed)
                .Select(i => new Step(MediaOperation.Update, i))
        );

        if (prune)
        {
            var kept = AncestorsOfKept(diff);
            // Removed items are in live pre-order; reversed, children go before their parents.
            for (var i = items.Count - 1; i >= 0; i--)
                if (items[i].Change == ContentChangeKind.Removed && !kept.Contains(items[i].Id))
                    plan.Add(new Step(MediaOperation.Trash, items[i]));
        }
        return plan;
    }

    /// <summary>
    /// The removed items with a kept item somewhere under them: trashing one would move the kept
    /// item to the recycle bin with it.
    /// </summary>
    /// <param name="diff">The diff, with the live parents.</param>
    /// <returns>The ids of removed items to leave alone.</returns>
    private static HashSet<Guid> AncestorsOfKept(MediaDiff diff)
    {
        var removed = diff.Removed.Select(r => r.Id).ToHashSet();
        var kept = new HashSet<Guid>();
        foreach (var id in diff.LiveParents.Keys.Where(id => !removed.Contains(id)))
        foreach (var ancestor in SnapshotTree.Lineage(id, diff.LiveParents).Skip(1))
            if (removed.Contains(ancestor))
                kept.Add(ancestor);
        return kept;
    }

    /// <summary>Runs one step.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="snapshot">The snapshot, for its files.</param>
    /// <param name="step">The step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write result.</returns>
    private static async Task<UmbracoResponse<Empty>> ExecuteAsync(
        IUmbracoManagementClient client,
        MediaSnapshot snapshot,
        Step step,
        CancellationToken ct
    )
    {
        var change = step.Change;
        if (step.Operation == MediaOperation.Trash)
            return await client.TrashMediaAsync(change.Id, ct);

        // A new or changed file is staged first, and the write points umbracoFile at it.
        var body = change.DesiredBody!;
        if (change.FileChanged)
        {
            var staged = await StageAsync(client, snapshot, change.File!, ct);
            if (!staged.IsSuccess)
                return UmbracoResponse<Empty>.Failure(staged.StatusCode, staged.ErrorMessage!);
            body = MediaBody.WithStagedFile(body, staged.Data);
        }
        else if (step.Operation == MediaOperation.Update)
            body = MediaBody.WithLiveFile(body, change.CurrentBody!);

        return step.Operation == MediaOperation.Create
            ? await client.CreateMediaRawAsync(SnapshotTree.WithParent(body, change.Parent), ct)
            : await client.UpdateMediaRawAsync(change.Id, body, ct);
    }

    /// <summary>Uploads a snapshot file to the temporary-file endpoint.</summary>
    /// <param name="client">The management client.</param>
    /// <param name="snapshot">The snapshot the file belongs to.</param>
    /// <param name="file">The file entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The temporary file id, or a failure when the file is missing from the snapshot.</returns>
    private static async Task<UmbracoResponse<Guid>> StageAsync(
        IUmbracoManagementClient client,
        MediaSnapshot snapshot,
        MediaFile file,
        CancellationToken ct
    )
    {
        var path = snapshot.PathOf(file);
        if (!File.Exists(path))
            return UmbracoResponse<Guid>.Failure(
                400,
                $"The snapshot is missing the file '{file.Path}'. Re-export it."
            );
        await using var stream = File.OpenRead(path);
        return await client.StageTemporaryFileAsync(
            stream,
            file.Name,
            MediaUploadCommand.MimeTypeFor(Path.GetExtension(file.Name)),
            ct
        );
    }
}
