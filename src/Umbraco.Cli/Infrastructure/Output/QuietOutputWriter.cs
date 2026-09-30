using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Decorates an <see cref="IOutputWriter"/> for <c>--quiet</c> (#94, #347). The contract
/// (docs/conventions.md 6.2): <c>--quiet</c> drops the result of a write - its confirmation and its
/// <c>data</c> - so a successful write prints nothing; a read still prints what it read. Errors,
/// dry-run previews and exit codes are never suppressed.
/// </summary>
/// <param name="inner">The underlying writer to delegate to.</param>
/// <param name="isWrite">
/// Whether the running command is a write (declared mutating, as <c>umbraco commands</c> reports
/// it). When true every success result is dropped; when false only the fixed
/// <see cref="WriteMessage"/> confirmations are, since those carry no requested data.
/// </param>
/// <remarks>
/// If a new success-style method is added to <see cref="IOutputWriter"/>, decide here whether it
/// is a result - a method left on the default pass-through would leak under <c>--quiet</c>.
/// </remarks>
public sealed class QuietOutputWriter(IOutputWriter inner, bool isWrite = false) : IOutputWriter
{
    /// <inheritdoc />
    /// <remarks>
    /// Forwarded, not left on the interface's no-op default: the fields belong to whatever the inner
    /// writer still emits (#440).
    /// </remarks>
    public void AddMeta(string key, object? value) => inner.AddMeta(key, value);

    /// <inheritdoc />
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        // A write's result (the created/updated item, or its { id }) is dropped; a read's is its data.
        if (!isWrite)
            inner.WriteSuccess(data, commandName, durationMs);
    }

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
    )
    {
        if (!isWrite)
            inner.WriteTable(headers, rows, commandName, durationMs);
    }

    /// <inheritdoc />
    public void WriteList(
        IReadOnlyList<object> items,
        string[] headers,
        IEnumerable<string[]> rows,
        ListPaging paging,
        string? commandName = null,
        long? durationMs = null
    )
    {
        // A write reporting a list (e.g. an apply report) is still a write result.
        if (!isWrite)
            inner.WriteList(items, headers, rows, paging, commandName, durationMs);
    }

    /// <inheritdoc />
    public void WriteMessage(
        object data,
        string message,
        string? commandName = null,
        long? durationMs = null
    )
    {
        // Suppressed under --quiet for every command: a fixed success confirmation is chatter,
        // and for a write its data is the write's result.
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
    )
    {
        // A bulk run is always a write. Its report is dropped only when every item succeeded:
        // with a failure it is how the run reports its errors, which --quiet never hides.
        if (!isWrite || results.Any(r => r.Status != BulkItemStatus.Success))
            inner.WriteBulk(results, commandName, durationMs);
    }
}
