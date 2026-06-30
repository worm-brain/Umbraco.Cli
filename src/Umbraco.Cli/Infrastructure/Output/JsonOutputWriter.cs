using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Output;

public sealed class JsonOutputWriter : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        var envelope = new
        {
            status = "success",
            data,
            meta = new
            {
                command = commandName,
                durationMs,
                timestamp = DateTimeOffset.UtcNow,
            },
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteError(int code, string message)
    {
        var envelope = new
        {
            status = "error",
            code,
            message,
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteTable(string[] headers, IEnumerable<string[]> rows)
    {
        // In JSON mode, render the table as an array of objects keyed by header name.
        var objects = rows.Select(row =>
        {
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < headers.Length && i < row.Length; i++)
                dict[headers[i]] = row[i];
            return dict;
        });
        WriteSuccess(objects);
    }

    public void WriteMessage(string message)
    {
        var envelope = new { status = "success", message };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }
}
