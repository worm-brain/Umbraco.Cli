using Spectre.Console;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Infrastructure.Output;

public sealed class HumanOutputWriter : IOutputWriter
{
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        // For raw objects, pretty-print via Spectre markup.
        AnsiConsole.MarkupLine($"[green]✓[/] Done");
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
        // The HTTP status is the more informative of the two when there is one, so it leads.
        var shown = httpStatus ?? (int)exitCode;
        AnsiConsole.MarkupLine($"[red]✗ Error {shown}:[/] {Markup.Escape(message)}");
        // Show the server version when it is known (#152): it is the single most useful bit of
        // triage context on a failure - which server produced it. Category is left to the
        // structured (JSON) output; the human line stays terse.
        if (!string.IsNullOrWhiteSpace(serverVersion))
            AnsiConsole.MarkupLine($"[grey]  Umbraco server:[/] {Markup.Escape(serverVersion)}");
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

        AnsiConsole.Write(table);
    }

    public void WriteMessage(string message, string? commandName = null, long? durationMs = null)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
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
            AnsiConsole.MarkupLine(line);
        }
        var s = BulkSummary.Of(results);
        AnsiConsole.MarkupLine(
            $"{s.Succeeded} succeeded, {s.Failed} failed"
                + (s.DryRun > 0 ? $", {s.DryRun} previewed" : "")
        );
    }

    public void WriteDryRun(
        string method,
        string url,
        string? body,
        string? commandName,
        long? durationMs = null
    )
    {
        AnsiConsole.MarkupLine(
            "[yellow]● DRY RUN[/] — the following request would be sent (nothing was executed):"
        );
        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(method)}[/] {Markup.Escape(url)}");
        if (!string.IsNullOrWhiteSpace(body))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(body!);
        }
    }
}
