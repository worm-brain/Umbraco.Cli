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
        // Serialize to a node first so --fields projection (#63) can trim the data before it is
        // emitted; without projection this is equivalent to serializing the value directly.
        var dataNode = JsonSerializer.SerializeToNode(data, Options);
        if (_fields is not null)
            dataNode = Project(dataNode, _fields);

        var envelope = new
        {
            status = "success",
            data = dataNode,
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

    /// <summary>
    /// Projects a data node to the requested <paramref name="fields"/> (#63): an array projects
    /// each element; an object keeps only the named fields, in the order requested, matched
    /// case-insensitively (so <c>--fields id,name</c> matches both <c>id</c> and a table's
    /// <c>ID</c> header). Non-object nodes are returned unchanged.
    /// </summary>
    /// <param name="node">The serialized data node.</param>
    /// <param name="fields">The field names to keep, in order.</param>
    /// <returns>The projected node.</returns>
    private static JsonNode? Project(JsonNode? node, string[] fields)
    {
        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
                result.Add(ProjectObject(item, fields));
            return result;
        }
        return ProjectObject(node, fields);
    }

    private static JsonNode? ProjectObject(JsonNode? node, string[] fields)
    {
        if (node is not JsonObject obj)
            return node?.DeepClone();

        var result = new JsonObject();
        foreach (var field in fields)
        {
            // Preserve the object's actual key casing but honour the requested order.
            var match = obj.FirstOrDefault(kv =>
                string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase)
            );
            if (match.Key is not null && !result.ContainsKey(match.Key))
                result[match.Key] = match.Value?.DeepClone();
        }
        return result;
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
