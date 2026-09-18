using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Output;

public sealed class JsonOutputWriter : IOutputWriter
{
    // Optional field projection (#63): when set, only these fields are kept on the data object
    // (or each array item), in this order. Matched case-insensitively. Null = no projection.
    private readonly string[]? _fields;

    /// <summary>Creates the writer, optionally projecting output data to a set of fields (#63).</summary>
    /// <param name="fields">Field names to keep (in order), or null for no projection.</param>
    public JsonOutputWriter(string[]? fields = null) =>
        _fields = fields is { Length: > 0 } ? fields : null;

    /// <summary>
    /// Version of the JSON output envelope (#61). Emitted as <c>meta.schemaVersion</c> so an
    /// agent can gate on the contract. Bump ONLY on a breaking change to the envelope — a
    /// renamed/removed field or a changed meaning. Field names are part of the contract and are
    /// never renamed silently. Additive fields do not bump it.
    ///
    /// History: "1" initial; "2" table JSON keys switched from human headers ("Content Type")
    /// to camelCase ("contentType") so list output agrees with object/get output (#87).
    /// </summary>
    public const string SchemaVersion = "2";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        // Without --fields, serialize the value directly (no extra DOM copy). With --fields,
        // serialize to a node first so the projection (#63) can trim it before it is emitted.
        object? payload = _fields is null
            ? data
            : OutputShaping.Project(JsonSerializer.SerializeToNode(data, Options), _fields);

        var envelope = new
        {
            status = "success",
            data = payload,
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

    public void WriteError(
        int code,
        string message,
        string? category = null,
        string? serverVersion = null
    )
    {
        // category/serverVersion are additive fields (#152); null ones are dropped by the
        // WhenWritingNull policy, so a policy error (no category) keeps the original shape.
        var envelope = new
        {
            status = "error",
            code,
            message,
            category,
            serverVersion,
            schemaVersion = SchemaVersion,
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    ) =>
        // Render the table as an array of objects keyed by the camelCased headers so list output
        // uses the same field keys as object/get output (#87). Passing command/duration through
        // keeps meta identical to object output (#137). Shared shaping with the CSV writer.
        WriteSuccess(OutputShaping.TableToRecords(headers, rows), commandName, durationMs);

    public void WriteMessage(string message, string? commandName = null, long? durationMs = null)
    {
        var envelope = new
        {
            status = "success",
            message,
            // Same meta shape as WriteSuccess so every success envelope agrees (#137).
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
