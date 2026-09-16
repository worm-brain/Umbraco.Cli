namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Decorates an <see cref="IOutputWriter"/> for <c>--quiet</c> (#94): it suppresses success
/// "chatter" (fixed <see cref="WriteMessage"/> confirmations like "Deleted.") while preserving the
/// requested data (<see cref="WriteSuccess"/>/<see cref="WriteTable"/>), errors, dry-run previews,
/// and exit codes - so a scripted caller still gets its output and can rely on the exit code.
/// </summary>
/// <param name="inner">The underlying writer to delegate to.</param>
public sealed class QuietOutputWriter(IOutputWriter inner) : IOutputWriter
{
    /// <inheritdoc />
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null) =>
        inner.WriteSuccess(data, commandName, durationMs);

    /// <inheritdoc />
    public void WriteError(int code, string message) => inner.WriteError(code, message);

    /// <inheritdoc />
    public void WriteTable(string[] headers, IEnumerable<string[]> rows) =>
        inner.WriteTable(headers, rows);

    /// <inheritdoc />
    public void WriteMessage(string message)
    {
        // Suppressed under --quiet: a fixed success confirmation is chatter, not data.
    }

    /// <inheritdoc />
    public void WriteDryRun(string method, string url, string? body) =>
        inner.WriteDryRun(method, url, body);
}
