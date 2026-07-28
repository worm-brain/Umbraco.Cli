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
            : Project(JsonSerializer.SerializeToNode(data, Options), _fields);

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
            // Prefer an exact key match; otherwise match ignoring case and spaces so
            // `--fields contentType` also selects a table's "Content Type" header. The object's
            // actual key casing is preserved, in the requested order.
            var match = obj.FirstOrDefault(kv => kv.Key == field) is { Key: not null } exact
                ? exact
                : obj.FirstOrDefault(kv => NormalizeKey(kv.Key) == NormalizeKey(field));
            if (match.Key is not null && !result.ContainsKey(match.Key))
                result[match.Key] = match.Value?.DeepClone();
        }
        return result;
    }

    /// <summary>Lower-cases and strips whitespace so "Content Type" and "contentType" match.</summary>
    private static string NormalizeKey(string key) =>
        new string(key.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

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
        // In JSON mode, render the table as an array of objects. Keys are the camelCased header
        // names ("Content Type" -> "contentType") so list output uses the same field keys as
        // object/get output, rather than the human header text (#87). Computed once per call.
        var keys = headers.Select(HeaderToCamelKey).ToArray();
        // Defensive: two headers whose camelCase keys collide (e.g. "ID" and "Id", or
        // "Content Type" and "ContentType") would silently overwrite a column. No current
        // command has such a pair; fail loudly so a future one is caught in tests, not in prod.
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new InvalidOperationException(
                $"Table headers produced duplicate JSON keys ({string.Join(", ", headers)}). "
                    + "Rename a header so each column's camelCase key is unique."
            );
        var objects = rows.Select(row =>
        {
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < keys.Length && i < row.Length; i++)
                dict[keys[i]] = row[i];
            return dict;
        });
        WriteSuccess(objects);
    }

    /// <summary>
    /// Converts a human table header to a camelCase JSON key so list output agrees with
    /// object/get output (#87): "ID" -> "id", "Content Type" -> "contentType", "Version ID" ->
    /// "versionId", "IsElement" -> "isElement". Words split on whitespace; all-caps acronyms are
    /// title-cased ("ID" -> "Id") before the first word is lower-cased and the rest Pascal-cased.
    /// </summary>
    /// <param name="header">The human-readable column header.</param>
    /// <returns>The camelCase key for that column.</returns>
    private static string HeaderToCamelKey(string header)
    {
        var words = header.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (words.Length == 0)
            return header;

        var sb = new System.Text.StringBuilder(header.Length);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            // Collapse an all-caps acronym ("ID") to title case ("Id") so casing is predictable.
            if (w.All(char.IsUpper))
                w = char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
            // First word is lower-cased (camel); subsequent words are Pascal-cased.
            w =
                i == 0
                    ? char.ToLowerInvariant(w[0]) + w[1..]
                    : char.ToUpperInvariant(w[0]) + w[1..];
            sb.Append(w);
        }
        return sb.ToString();
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
