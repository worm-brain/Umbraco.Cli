using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>
/// Reads the ids a bulk operation should act on (#85) from a file (<c>--file</c>) or, when no
/// file is given, from stdin - so the CLI's own output pipes straight in
/// (<c>umbraco content list --fields id | umbraco content bulk delete --yes</c>, #288).
/// The format is decided from the content:
/// <list type="bullet">
///   <item>JSON (first non-whitespace character <c>{</c> or <c>[</c>): the CLI's success envelope
///   (the <c>id</c> of each <c>data</c> item), a bare array of objects with <c>id</c>, or an array
///   of id strings;</item>
///   <item>CSV whose first line is a header with an <c>id</c> column (as <c>-o csv</c> writes
///   it): that column, header skipped;</item>
///   <item>anything else: one id per line, blank lines and surrounding whitespace ignored.</item>
/// </list>
/// Input that is recognisably JSON or CSV but malformed throws <see cref="InvalidInputException"/>,
/// so the whole batch is refused as <c>invalid_argument</c>. Validation of each id is left to the
/// executor, so a malformed id is reported per item rather than aborting the batch.
/// </summary>
public static class BulkIds
{
    /// <summary>The field or column that carries an item's id in JSON and CSV input.</summary>
    private const string IdField = "id";

    /// <summary>
    /// Reads the ids from <paramref name="file"/>, or from stdin when it is null or <c>-</c>
    /// (docs/conventions.md 4.5, #312). Any IO error
    /// propagates to the caller (the executor reports it via the output writer).
    /// </summary>
    /// <param name="file">The file to read ids from, or null / <c>-</c> to read stdin.</param>
    /// <returns>The ids in input order.</returns>
    /// <exception cref="InvalidInputException">The input is malformed JSON or CSV.</exception>
    /// <exception cref="IOException">The file or stdin could not be read.</exception>
    public static IReadOnlyList<string> Read(FileInfo? file)
    {
        // Both sources are read as UTF-8 with any byte-order mark stripped: stdin through the
        // reader every '-' input shares, a file the way File.ReadAllText reads one. Console.In
        // decodes with the console code page instead, so a producer that writes a BOM (PowerShell,
        // .NET's Encoding.UTF8) turned the first id into garbage that failed as "not a valid GUID".
        using var reader = ReadsStdin(file)
            ? StandardInput.OpenText()
            : File.OpenText(file!.FullName);
        return Parse(reader);
    }

    /// <summary>
    /// Whether <paramref name="file"/> means stdin: no <c>--file</c>, or <c>--file -</c>, the token
    /// every file input uses for stdin (#312). The check is on the path as typed, because
    /// <see cref="FileInfo.FullName"/> would turn <c>-</c> into a file of that name.
    /// </summary>
    /// <param name="file">The <c>--file</c> value, or null.</param>
    /// <returns>True to read stdin.</returns>
    internal static bool ReadsStdin(FileInfo? file) =>
        file is null || file.ToString() == Infrastructure.JsonBodyInput.StdinToken;

    /// <summary>Reads the ids from <paramref name="reader"/>, in whichever format it holds.</summary>
    /// <param name="reader">The reader, e.g. over stdin.</param>
    /// <returns>The ids in input order.</returns>
    /// <exception cref="InvalidInputException">The input is malformed JSON or CSV.</exception>
    internal static IReadOnlyList<string> Parse(TextReader reader) => Parse(reader.ReadToEnd());

    /// <summary>Picks the input format from the content and reads the ids out of it.</summary>
    /// <param name="text">The whole input.</param>
    /// <returns>The ids in input order.</returns>
    /// <exception cref="InvalidInputException">The input is malformed JSON or CSV.</exception>
    private static IReadOnlyList<string> Parse(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return ParseJson(trimmed);

        return CsvIdColumn(trimmed) is { } column ? ParseCsv(text, column) : ParseLines(text);
    }

    /// <summary>
    /// Reads ids from JSON: the CLI's envelope (<c>data</c> an array, or one object for a single
    /// item), or a bare array. Each item is an id string or an object with a string <c>id</c>.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The ids in input order.</returns>
    /// <exception cref="InvalidInputException">
    /// The JSON does not parse, has no <c>data</c>, or an item carries no string id.
    /// </exception>
    private static IReadOnlyList<string> ParseJson(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidInputException(
                $"The input looks like JSON but does not parse: {ex.Message}"
            );
        }

        // The envelope wraps the items in `data`; a bare array is the items itself. The path
        // prefix names the item in errors the way jq would address it.
        var (items, path) = root switch
        {
            JsonArray array => (array, ""),
            JsonObject obj when obj["data"] is JsonArray data => (data, ".data"),
            // `data` as one object is a single-item command's output (get, create).
            JsonObject obj when obj["data"] is JsonObject single => (
                new JsonArray(single.DeepClone()),
                ".data"
            ),
            _ => throw new InvalidInputException(
                "The JSON input has no data array. Pipe in the JSON output of a list command, "
                    + "an array of objects with an id, or an array of ids."
            ),
        };

        return items
            .Select(
                (item, i) =>
                    IdOf(item)
                    ?? throw new InvalidInputException(
                        $"The JSON input item {path}[{i}] has no string id. Select it with "
                            + "--fields id, or pipe in an array of ids."
                    )
            )
            .ToList();
    }

    /// <summary>The id a JSON item carries: the item itself when it is a string, else its <c>id</c>.</summary>
    /// <param name="item">The array item.</param>
    /// <returns>The trimmed id, or null when the item has no string id.</returns>
    private static string? IdOf(JsonNode? item)
    {
        var value = item is JsonObject obj ? obj[IdField] : item;
        return value is JsonValue v && v.TryGetValue<string>(out var id) ? id.Trim() : null;
    }

    /// <summary>
    /// The index of the <c>id</c> column when the first non-blank line is a CSV header that has
    /// one. A plain id never reads as <c>id</c>, so a one-id-per-line input is not mistaken for it.
    /// </summary>
    /// <param name="text">The input, leading whitespace already removed.</param>
    /// <returns>The column index, or null when the input is not CSV with an id column.</returns>
    private static int? CsvIdColumn(string text)
    {
        var firstLine = text.Split('\n', 2)[0].TrimEnd('\r');
        if (!TryReadCsv(firstLine, out var records) || records.Count == 0)
            return null;

        var index = Array.FindIndex(
            records[0],
            h => string.Equals(h.Trim(), IdField, StringComparison.OrdinalIgnoreCase)
        );
        return index < 0 ? null : index;
    }

    /// <summary>Reads the <paramref name="column"/> cell of each CSV row after the header.</summary>
    /// <param name="text">The CSV text.</param>
    /// <param name="column">The index of the id column.</param>
    /// <returns>The ids in input order.</returns>
    /// <exception cref="InvalidInputException">
    /// A quoted field is never closed, or a row has no id in the id column.
    /// </exception>
    private static IReadOnlyList<string> ParseCsv(string text, int column)
    {
        if (!TryReadCsv(text, out var records))
            throw new InvalidInputException(
                "The CSV input has a quoted field that is never closed."
            );

        // Row numbers count the header as row 1, as a spreadsheet would show them.
        return records
            .Skip(1)
            .Select(
                (row, i) =>
                    (column < row.Length ? row[column].Trim() : "") is { Length: > 0 } id
                        ? id
                        : throw new InvalidInputException(
                            $"The CSV input row {i + 2} has no value in the id column."
                        )
            )
            .ToList();
    }

    /// <summary>Reads one id per line, trimmed, skipping blank lines.</summary>
    /// <param name="text">The input.</param>
    /// <returns>The ids in input order.</returns>
    private static IReadOnlyList<string> ParseLines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

    /// <summary>
    /// Splits CSV text into records by the RFC 4180 rules <c>CsvOutputWriter</c> writes: fields
    /// separated by commas, a field holding a comma, quote or newline wrapped in double quotes,
    /// and a quote inside one doubled. Records end at CRLF or LF; blank records are dropped.
    /// </summary>
    /// <param name="text">The CSV text.</param>
    /// <param name="records">The records, each an array of field values.</param>
    /// <returns>False when a quoted field is never closed.</returns>
    private static bool TryReadCsv(string text, out List<string[]> records)
    {
        records = [];
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                // Inside quotes everything is literal except a quote: doubled it is one quote,
                // single it closes the field.
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    field.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
                fields.Add(TakeField(field));
            else if (c is '\n' or '\r')
            {
                // CRLF is one line break, not a blank record between two.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                EndRecord(records, fields, field);
            }
            else
                field.Append(c);
        }

        EndRecord(records, fields, field);
        return !quoted;
    }

    /// <summary>Closes the current record, keeping it unless it is blank.</summary>
    /// <param name="records">The records read so far.</param>
    /// <param name="fields">The current record's completed fields; cleared.</param>
    /// <param name="field">The current field's text; cleared.</param>
    private static void EndRecord(List<string[]> records, List<string> fields, StringBuilder field)
    {
        fields.Add(TakeField(field));
        if (fields.Count > 1 || fields[0].Trim().Length > 0)
            records.Add([.. fields]);
        fields.Clear();
    }

    /// <summary>Returns the current field's text and clears it for the next field.</summary>
    /// <param name="field">The current field's text.</param>
    /// <returns>The field value.</returns>
    private static string TakeField(StringBuilder field)
    {
        var value = field.ToString();
        field.Clear();
        return value;
    }
}
