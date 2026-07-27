namespace Umbraco.Cli.Infrastructure.Output;

public enum OutputFormat
{
    Human,
    Json,
}

public interface IOutputWriter
{
    void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null);
    void WriteError(int code, string message);
    void WriteTable(string[] headers, IEnumerable<string[]> rows);
    void WriteMessage(string message);

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
