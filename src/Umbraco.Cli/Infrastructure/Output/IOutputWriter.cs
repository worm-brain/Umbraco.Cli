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
}
