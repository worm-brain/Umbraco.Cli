using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;

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
    /// to camelCase ("contentType") so list output agrees with object/get output (#87); "3" list
    /// output is serialized from the DTOs instead of the human table cells, so field names and
    /// types now match the matching `get` exactly (#164) - booleans are booleans, `published`
    /// became `isPublished`; the error envelope moved `schemaVersion` into `meta` and split the
    /// overloaded `code` into `exitCode` + `httpStatus` (#177); and the `--dry-run` payload moved
    /// from `request` to `data` (#165); "4" a bulk run's `status` follows its outcomes - `partial`
    /// when some items failed, `error` when all did, `dry-run` when previewed - instead of always
    /// `success`, with counts in `meta.summary` and each dry-run item's request on the item (#236);
    /// "5" the diff and apply reports (`content diff`, `schema diff`, `content apply`,
    /// `schema apply`) are serialized from their records instead of caption-keyed strings -
    /// `idMismatch` is a boolean, an empty id/parent/note is null rather than `""`, rows carry a
    /// `changes` list and `meta.total`, and `content apply` rows carry `cultures` (#229); and a
    /// member's `groups` are `{id, name}` objects instead of bare ids (#212). Both changed before
    /// "5" was released, so they share it.
    /// </summary>
    public const string SchemaVersion = "5";

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
        ExitCode exitCode,
        FailureCategory category,
        string message,
        string? commandName,
        int? httpStatus = null,
        string? serverVersion = null
    )
    {
        // serverVersion (#152) and httpStatus are dropped by the WhenWritingNull policy when
        // they do not apply; category is always present.
        var envelope = new
        {
            status = "error",
            // #177: `code` used to be the CLI exit code sometimes and the HTTP status other
            // times, so it could not be acted on without knowing which. They are separate fields
            // now; `httpStatus` is absent when the failure never reached the server.
            exitCode = (int)exitCode,
            httpStatus,
            message,
            category = category.ToWire(),
            serverVersion,
            meta = new
            {
                command = commandName,
                timestamp = DateTimeOffset.UtcNow,
                schemaVersion = SchemaVersion,
            },
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteList(
        IReadOnlyList<object> items,
        string[] headers,
        IEnumerable<string[]> rows,
        ListPaging paging,
        string? commandName = null,
        long? durationMs = null
    )
    {
        // #164: the DTOs, not the table cells - so keys and types match the matching `get`.
        object? payload = _fields is null
            ? items
            : OutputShaping.Project(JsonSerializer.SerializeToNode(items, Options), _fields);

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
                // #173: absent rather than guessed when the source cannot say how many there are.
                total = paging.Total,
                skip = paging.Skip,
                take = paging.Take,
                hasMore = paging.HasMoreAfter(items.Count),
            },
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
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

    public void WriteMessage(
        object data,
        string message,
        string? commandName = null,
        long? durationMs = null
    ) =>
        // The message is for people; structured output carries the data, in the same envelope as
        // every other success (docs/conventions.md 6.2).
        WriteSuccess(data, commandName, durationMs);

    /// <inheritdoc />
    public void WriteBulk(
        IReadOnlyList<BulkItemResult> results,
        string? commandName = null,
        long? durationMs = null
    )
    {
        object? payload = _fields is null
            ? results
            : OutputShaping.Project(JsonSerializer.SerializeToNode(results, Options), _fields);
        var summary = BulkSummary.Of(results);

        // #236: the status follows the outcomes, so it no longer says "success" while the exit
        // code says 1. meta.summary gives the counts without walking data.
        var envelope = new
        {
            status = summary.Status,
            data = payload,
            meta = new
            {
                command = commandName,
                durationMs,
                timestamp = DateTimeOffset.UtcNow,
                schemaVersion = SchemaVersion,
                summary = new
                {
                    succeeded = summary.Succeeded,
                    failed = summary.Failed,
                    dryRun = summary.DryRun,
                },
            },
        };
        Console.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteDryRun(
        string method,
        string url,
        string? body,
        string? commandName,
        long? durationMs = null
    )
    {
        // Embed the body as parsed JSON when it is valid JSON so the preview nests cleanly
        // for agents; otherwise fall back to the raw string. A dry run is a successful
        // preview, so it goes to stdout with a distinct "dry-run" status.
        var parsedBody = JsonBody.Parse(body);

        // #165: under `data`, like every other success envelope. It used to be `request`, which
        // was the one documented exception to "the payload always lives under .data".
        var envelope = new
        {
            status = "dry-run",
            data = new
            {
                method,
                url,
                body = parsedBody,
            },
            // The same meta as every other envelope (docs/conventions.md, section 6).
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
}
