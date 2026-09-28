using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Decorates an <see cref="IOutputWriter"/> for <c>--quiet</c> (#94): it suppresses success
/// "chatter" (fixed <see cref="WriteMessage"/> confirmations like "Deleted.") while preserving the
/// requested data (<see cref="WriteSuccess"/>/<see cref="WriteTable"/>), errors, dry-run previews,
/// and exit codes - so a scripted caller still gets its output and can rely on the exit code.
/// </summary>
/// <param name="inner">The underlying writer to delegate to.</param>
/// <remarks>
/// This decorator suppresses ONLY <see cref="WriteMessage"/>; everything else passes through. If a
/// new "chatter"-style method is added to <see cref="IOutputWriter"/>, suppress it here too - a new
/// method left on the default pass-through would leak under <c>--quiet</c>.
/// </remarks>
public sealed class QuietOutputWriter(IOutputWriter inner) : IOutputWriter
{
    /// <inheritdoc />
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null) =>
        inner.WriteSuccess(data, commandName, durationMs);

    /// <inheritdoc />
    public void WriteError(
        ExitCode exitCode,
        FailureCategory category,
        string message,
        string? commandName,
        int? httpStatus = null,
        string? serverVersion = null,
        JsonNode? details = null
    ) =>
        inner.WriteError(
            exitCode,
            category,
            message,
            commandName,
            httpStatus,
            serverVersion,
            details
        );

    /// <inheritdoc />
    public void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    ) => inner.WriteTable(headers, rows, commandName, durationMs);

    /// <inheritdoc />
    public void WriteList(
        IReadOnlyList<object> items,
        string[] headers,
        IEnumerable<string[]> rows,
        ListPaging paging,
        string? commandName = null,
        long? durationMs = null
    ) => inner.WriteList(items, headers, rows, paging, commandName, durationMs);

    /// <inheritdoc />
    public void WriteMessage(
        object data,
        string message,
        string? commandName = null,
        long? durationMs = null
    )
    {
        // Suppressed under --quiet: a fixed success confirmation is chatter, not data.
    }

    /// <inheritdoc />
    public void WriteDryRun(
        string method,
        string url,
        string? body,
        string? commandName,
        long? durationMs = null,
        IReadOnlyList<Umbraco.Cli.Infrastructure.Http.PreviewedRequest>? then = null
    ) => inner.WriteDryRun(method, url, body, commandName, durationMs, then);

    /// <inheritdoc />
    public void WriteBulk(
        IReadOnlyList<BulkItemResult> results,
        string? commandName = null,
        long? durationMs = null
    ) => inner.WriteBulk(results, commandName, durationMs);
}
