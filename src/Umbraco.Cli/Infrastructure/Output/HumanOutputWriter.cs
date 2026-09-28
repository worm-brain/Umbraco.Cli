using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Rendering;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// The terminal writer (<c>--output human</c>, the default on a TTY): Spectre.Console tables and
/// one-line confirmations for a person to read.
/// </summary>
public sealed class HumanOutputWriter : IOutputWriter
{
    // Same shape as the JSON writer's data (camelCase, nulls left out), so the keys a person
    // reads here are the keys a script gets from -o json. Not indented: nested values are shown
    // compactly inside a single cell.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IAnsiConsole? _console;

    /// <summary>Creates the writer.</summary>
    /// <param name="console">
    /// The console to write to, or null for the process-wide <see cref="AnsiConsole.Console"/>
    /// (resolved on each write, so <see cref="ConsoleColorSetup"/> still applies). Tests pass
    /// their own to capture the output.
    /// </param>
    public HumanOutputWriter(IAnsiConsole? console = null) => _console = console;

    private IAnsiConsole Out => _console ?? AnsiConsole.Console;

    /// <summary>
    /// Renders an object result (a <c>get</c>, or the item a <c>create</c>/<c>update</c> returns)
    /// so a person sees its data, including a new item's id (#348). An object becomes a
    /// key/value grid, a list of objects a table, a scalar its value; nested values are shown as
    /// compact JSON. "Done" is kept only for a result with nothing in it.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="data">The result to render.</param>
    /// <param name="commandName">Unused: the human output has no <c>meta</c>.</param>
    /// <param name="durationMs">Unused: the human output has no <c>meta</c>.</param>
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        var node = JsonSerializer.SerializeToNode(data, Options);
        switch (node)
        {
            case null:
            case JsonObject { Count: 0 }:
            case JsonArray { Count: 0 }:
                Out.MarkupLine("[green]✓[/] Done");
                break;
            case JsonObject obj:
                Out.Write(KeyValueGrid(obj));
                break;
            case JsonArray array when array.All(i => i is JsonObject):
                Out.Write(ObjectTable(array.Cast<JsonObject>().ToList()));
                break;
            case JsonArray array:
                // A list of scalars (ids, names): one per line, easy to copy or pipe.
                foreach (var item in array)
                    Out.WriteLine(CellText(item));
                break;
            default:
                // A bare scalar, e.g. the bool from property-type is-used.
                Out.WriteLine(CellText(node));
                break;
        }
    }

    /// <summary>One row per property: the key in grey, then its value as plain text.</summary>
    /// <param name="obj">The object to show.</param>
    /// <returns>The grid.</returns>
    private static Grid KeyValueGrid(JsonObject obj)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        foreach (var (key, value) in obj)
            grid.AddRow(
                new IRenderable[]
                {
                    new Markup($"[grey]{Markup.Escape(key)}[/]"),
                    new Text(CellText(value)),
                }
            );
        return grid;
    }

    /// <summary>
    /// A table with one row per object and one column per key seen on any of them, in first-seen
    /// order, so items with differing shapes still line up.
    /// </summary>
    /// <param name="items">The objects to show.</param>
    /// <returns>The table.</returns>
    private static Table ObjectTable(IReadOnlyList<JsonObject> items)
    {
        var keys = items.SelectMany(i => i.Select(p => p.Key)).Distinct().ToList();
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var k in keys)
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(k)}[/]"));
        foreach (var item in items)
            table.AddRow(
                keys.Select(k =>
                    (IRenderable)
                        new Text(CellText(item.TryGetPropertyValue(k, out var v) ? v : null))
                )
            );
        return table;
    }

    /// <summary>
    /// The text for one value: a string as-is (no quotes), another scalar as its JSON literal,
    /// an object or array as compact JSON, and null as empty.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text to show.</returns>
    private static string CellText(JsonNode? value) =>
        value switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => value.ToJsonString(Options),
        };

    /// <inheritdoc />
    public void WriteError(
        ExitCode exitCode,
        FailureCategory category,
        string message,
        string? commandName,
        int? httpStatus = null,
        string? serverVersion = null,
        JsonNode? details = null
    )
    {
        // The HTTP status is the more informative of the two when there is one, so it leads.
        var shown = httpStatus ?? (int)exitCode;
        Out.MarkupLine($"[red]✗ Error {shown}:[/] {Markup.Escape(message)}");
        // Show the server version when it is known (#152): it is the single most useful bit of
        // triage context on a failure - which server produced it. Category is left to the
        // structured (JSON) output; the human line stays terse.
        if (!string.IsNullOrWhiteSpace(serverVersion))
            Out.MarkupLine($"[grey]  Umbraco server:[/] {Markup.Escape(serverVersion)}");
    }

    /// <inheritdoc />
    public void WriteList(
        IReadOnlyList<object> items,
        string[] headers,
        IEnumerable<string[]> rows,
        ListPaging paging,
        string? commandName = null,
        long? durationMs = null
    )
    {
        WriteTable(headers, rows, commandName, durationMs);

        // #173: a truncated list used to look identical to a complete one. Say so, on stderr so
        // the table itself stays pipeable.
        if (
            paging.HasMoreAfter(items.Count) is true
            && paging is { Total: { } total, Skip: { } skip }
        )
            Console.Error.WriteLine(
                $"Showing {skip + items.Count} of {total}. Use --skip/--take to page, or --all."
            );
    }

    /// <inheritdoc />
    public void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    )
    {
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var h in headers)
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(h)}[/]"));

        foreach (var row in rows)
            table.AddRow(row.Select(Markup.Escape).ToArray());

        Out.Write(table);
    }

    /// <inheritdoc />
    public void WriteMessage(
        object data,
        string message,
        string? commandName = null,
        long? durationMs = null
    )
    {
        Out.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    /// <inheritdoc />
    public void WriteBulk(
        IReadOnlyList<BulkItemResult> results,
        string? commandName = null,
        long? durationMs = null
    )
    {
        // One line per item, then the counts: a plain "Done" hid every failure (#236).
        foreach (var r in results)
        {
            var line = r.Status switch
            {
                BulkItemStatus.Success => $"[green]✓[/] {Markup.Escape(r.Id)}",
                BulkItemStatus.DryRun when r.Request is { } q =>
                    $"[yellow]●[/] {Markup.Escape(r.Id)}  would send {Markup.Escape(q.Method)} {Markup.Escape(q.Url)}",
                BulkItemStatus.DryRun => $"[yellow]●[/] {Markup.Escape(r.Id)}  (dry run)",
                _ => $"[red]✗[/] {Markup.Escape(r.Id)}  {Markup.Escape(r.Error ?? "failed")}",
            };
            Out.MarkupLine(line);
        }
        var s = BulkSummary.Of(results);
        Out.MarkupLine(
            $"{s.Succeeded} succeeded, {s.Failed} failed"
                + (s.DryRun > 0 ? $", {s.DryRun} previewed" : "")
        );
    }

    /// <inheritdoc />
    public void WriteDryRun(
        string method,
        string url,
        string? body,
        string? commandName,
        long? durationMs = null
    )
    {
        Out.MarkupLine(
            "[yellow]● DRY RUN[/] — the following request would be sent (nothing was executed):"
        );
        Out.MarkupLine($"  [bold]{Markup.Escape(method)}[/] {Markup.Escape(url)}");
        if (!string.IsNullOrWhiteSpace(body))
        {
            Out.WriteLine();
            Out.WriteLine(body!);
        }
    }
}
