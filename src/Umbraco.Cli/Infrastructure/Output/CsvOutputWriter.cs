using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// CSV output writer (#93). CSV is inherently tabular, so <see cref="WriteTable"/> maps directly;
/// object/scalar success values are flattened to a header row plus a value row (nested values are
/// emitted as compact JSON in the cell). Errors go to stderr as a <c>code,message</c> pair.
/// </summary>
public sealed class CsvOutputWriter : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string[]? _fields;

    /// <summary>Creates the writer, optionally projecting output to a set of fields/columns (#63).</summary>
    /// <param name="fields">Field names to keep (in order), or null for no projection.</param>
    public CsvOutputWriter(string[]? fields = null) =>
        _fields = fields is { Length: > 0 } ? fields : null;

    /// <inheritdoc />
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        var node = OutputShaping.Project(JsonSerializer.SerializeToNode(data, Options), _fields);
        switch (node)
        {
            case JsonArray array:
                WriteRowsFromObjects(array, Console.Out);
                break;
            case JsonObject obj:
                WriteRowsFromObjects(new JsonArray(obj.DeepClone()), Console.Out);
                break;
            default:
                // A bare scalar (e.g. a bool from is-used): emit the value alone.
                Console.Out.WriteLine(Escape(CellValue(node)));
                break;
        }
    }

    /// <inheritdoc />
    public void WriteError(int code, string message)
    {
        Console.Error.WriteLine("code,message");
        Console.Error.WriteLine($"{Escape(code.ToString())},{Escape(message)}");
    }

    /// <inheritdoc />
    public void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    ) =>
        // Route through WriteSuccess so table columns use the same camelCase keys as object output
        // (#87 consistency) and honour --fields, exactly like the JSON writer. CSV has no meta, so
        // command/duration (#137) do not apply here.
        WriteSuccess(OutputShaping.TableToRecords(headers, rows));

    /// <inheritdoc />
    public void WriteMessage(string message, string? commandName = null, long? durationMs = null)
    {
        Console.Out.WriteLine("message");
        Console.Out.WriteLine(Escape(message));
    }

    /// <inheritdoc />
    public void WriteDryRun(string method, string url, string? body)
    {
        Console.Out.WriteLine("method,url,body");
        Console.Out.WriteLine($"{Escape(method)},{Escape(url)},{Escape(body ?? "")}");
    }

    /// <summary>
    /// Writes CSV rows for an array of objects: the header row is the union of keys (in first-seen
    /// order across all rows) and each object contributes one row, with missing keys left blank.
    /// </summary>
    /// <param name="array">The array of objects to render.</param>
    /// <param name="writer">The destination writer.</param>
    private static void WriteRowsFromObjects(JsonArray array, TextWriter writer)
    {
        var objects = array.OfType<JsonObject>().ToList();
        if (objects.Count == 0)
        {
            // Nothing tabular to render (empty array, or array of scalars).
            foreach (var item in array)
                writer.WriteLine(Escape(CellValue(item)));
            return;
        }

        var keys = objects.SelectMany(o => o.Select(kv => kv.Key)).Distinct().ToArray();
        writer.WriteLine(string.Join(",", keys.Select(Escape)));
        foreach (var obj in objects)
            writer.WriteLine(
                string.Join(
                    ",",
                    keys.Select(k =>
                        Escape(CellValue(obj.TryGetPropertyValue(k, out var v) ? v : null))
                    )
                )
            );
    }

    /// <summary>Renders a JSON node as a CSV cell: scalars as their text, objects/arrays as compact JSON.</summary>
    /// <param name="node">The node, or null.</param>
    /// <returns>The cell text.</returns>
    private static string CellValue(JsonNode? node) =>
        node switch
        {
            null => "",
            JsonValue value => value.ToString(),
            _ => node.ToJsonString(),
        };

    /// <summary>Quotes a CSV field when it contains a comma, quote, or newline (RFC 4180).</summary>
    /// <param name="field">The raw field value.</param>
    /// <returns>The escaped field.</returns>
    private static string Escape(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return field;
        return new StringBuilder(field.Length + 2)
            .Append('"')
            .Append(field.Replace("\"", "\"\""))
            .Append('"')
            .ToString();
    }
}
