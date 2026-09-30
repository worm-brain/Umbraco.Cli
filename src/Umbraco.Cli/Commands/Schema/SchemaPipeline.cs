using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The shared prelude for <c>schema diff</c> and <c>schema apply</c> (issue #68): load a
/// snapshot file, export the live schema, and compute the diff between them. Both commands need
/// exactly this before they diverge (one renders the diff, the other applies it), so it lives
/// here once rather than being copy-pasted into each command.
/// </summary>
public static class SchemaPipeline
{
    /// <summary>
    /// Loads the snapshot at <paramref name="snapshotPath"/> (or stdin for <c>-</c>), exports the
    /// live schema behind <paramref name="client"/>, and returns the diff of desired-vs-live.
    /// </summary>
    /// <param name="client">The authenticated management client.</param>
    /// <param name="snapshotPath">The snapshot file path, or <c>-</c> for stdin.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed diff, or the export failure. Snapshot load errors surface as exceptions the executor maps to a clean error.</returns>
    public static async Task<UmbracoResponse<SchemaDiff>> DiffAgainstLiveAsync(
        IUmbracoManagementClient client,
        string snapshotPath,
        CancellationToken ct
    )
    {
        var desired = await SchemaFile.LoadAsync(snapshotPath, ct);
        // Only the kinds the snapshot manages are read (#292, #198): a section the snapshot does
        // not have is left alone, so reading it would be wasted.
        var current = await SchemaExporter.ExportAsync(
            client,
            [.. SchemaKinds.All.Where(k => k.Section(desired) is not null)],
            ct
        );
        if (!current.IsSuccess)
            return UmbracoResponse<SchemaDiff>.FailureFrom(current);

        // #198: a hand-written snapshot may name what it references and leave ids out; both are
        // settled before the diff, so diff and apply see ids as an export would carry them.
        var normalised = await SchemaReferences.NormaliseAsync(desired, current.Data!, client, ct);
        if (!normalised.IsSuccess)
            return UmbracoResponse<SchemaDiff>.FailureFrom(normalised);

        var diff = SchemaDiffEngine.Compare(desired, current.Data!);
        // Stderr, as the CLI's other warnings, so stdout stays the diff or apply report (#442).
        if (FormatMismatch(desired, current.Data!, diff) is { } warning)
            Console.Error.WriteLine($"warning: {warning}");
        return UmbracoResponse<SchemaDiff>.Success(diff);
    }

    /// <summary>
    /// The warning for promoting dictionary values between sites that store them in different
    /// formats (#442), or null when there is nothing to warn about: either format is unknown, they
    /// match, or no dictionary item would be written. A warning, never a refusal - the CLI stores
    /// any value (ADR 0009).
    /// </summary>
    /// <param name="desired">The snapshot being promoted.</param>
    /// <param name="live">The target's live export.</param>
    /// <param name="diff">The diff between them.</param>
    /// <returns>The warning text, or null.</returns>
    public static string? FormatMismatch(
        SchemaSnapshot desired,
        SchemaSnapshot live,
        SchemaDiff diff
    )
    {
        var (from, to) = (desired.DictionaryValueFormat, live.DictionaryValueFormat);
        // Deletes write no values, so only creates and updates count.
        var written = diff.DictionaryItems.Added.Count + diff.DictionaryItems.Changed.Count;
        if (from is null || to is null || from == to || written == 0)
            return null;
        return $"the snapshot's dictionary values are '{from}' but this site stores '{to}'; "
            + $"{written} dictionary item(s) would be written as they are, without conversion.";
    }
}
