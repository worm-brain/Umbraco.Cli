using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Output;

public sealed class JsonOutputWriter : IOutputWriter
{
    /// <summary>
    /// Version of the JSON output envelope (#61). Emitted as <c>meta.schemaVersion</c> so an
    /// agent can gate on the contract. Bump ONLY on a breaking change to the envelope — a
    /// renamed/removed field or a changed meaning. Field names are part of the contract and are
    /// never renamed silently. Additive fields do not bump it.
    /// </summary>
    public const string SchemaVersion = "1";

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
                schemaVersion = SchemaVersion,
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
            schemaVersion = SchemaVersion,
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
        var envelope = new
        {
            status = "success",
            message,
            meta = new { schemaVersion = SchemaVersion },
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteDryRun(string method, string url, string? body)
    {
        // Embed the body as parsed JSON when it is valid JSON so the preview nests cleanly
        // for agents; otherwise fall back to the raw string. A dry run is a successful
        // preview, so it goes to stdout with a distinct "dry-run" status.
        object? parsedBody = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                parsedBody = JsonSerializer.Deserialize<JsonElement>(body!);
            }
            catch (JsonException)
            {
                parsedBody = body;
            }
        }

        var envelope = new
        {
            status = "dry-run",
            request = new
            {
                method,
                url,
                body = parsedBody,
            },
            meta = new { schemaVersion = SchemaVersion },
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }
}
