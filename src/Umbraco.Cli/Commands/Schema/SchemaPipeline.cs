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
        var current = await SchemaExporter.ExportAsync(client, ct);
        if (!current.IsSuccess)
            return UmbracoResponse<SchemaDiff>.Failure(current.StatusCode, current.ErrorMessage!);

        return UmbracoResponse<SchemaDiff>.Success(SchemaDiffEngine.Compare(desired, current.Data!));
    }
}
