namespace Umbraco.Cli.Infrastructure.Output;

public enum OutputFormat
{
    Human,
    Json,
    Csv,
}

public interface IOutputWriter
{
    void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null);

    /// <summary>
    /// Writes an error to stderr. <paramref name="category"/> and <paramref name="serverVersion"/>
    /// annotate an API failure (#152) so a caller can tell whose problem it is and against which
    /// server; both are optional and only the structured (JSON) writer emits them. Policy errors
    /// (auth, read-only, cancellation) call this without them.
    /// </summary>
    /// <param name="code">The error/exit code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="category">The failure category wire name (e.g. <c>server_error</c>), or null.</param>
    /// <param name="serverVersion">The connected server's version, or null when unknown/not applicable.</param>
    void WriteError(
        int code,
        string message,
        string? category = null,
        string? serverVersion = null
    );

    /// <summary>
    /// Renders a list result as a table. <paramref name="commandName"/> and
    /// <paramref name="durationMs"/> populate the same <c>meta</c> fields that
    /// <see cref="WriteSuccess"/> emits, so list output carries the same envelope contract as
    /// object output (#137) - a structured writer with no <c>meta</c> (CSV/human) ignores them.
    /// </summary>
    /// <param name="headers">Column headers (camelCased into field keys by structured writers).</param>
    /// <param name="rows">The row cells, aligned to <paramref name="headers"/>.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    );

    /// <summary>
    /// Renders a fixed success message. <paramref name="commandName"/> and
    /// <paramref name="durationMs"/> populate the same <c>meta</c> fields as
    /// <see cref="WriteSuccess"/>, so a message-style success (delete/publish) carries the same
    /// envelope contract (#137).
    /// </summary>
    /// <param name="message">The success message.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    void WriteMessage(string message, string? commandName = null, long? durationMs = null);

    /// <summary>
    /// Renders a <c>--dry-run</c> preview of the write request that would have been sent,
    /// without executing it. Written to stdout (it is a successful preview, not an error) so
    /// agents can read the intended request from the normal output stream.
    /// </summary>
    /// <param name="method">The HTTP method that would be sent (e.g. <c>POST</c>).</param>
    /// <param name="url">The absolute request URL.</param>
    /// <param name="body">The request body, or null for a body-less request (e.g. DELETE).</param>
    void WriteDryRun(string method, string url, string? body);
}
