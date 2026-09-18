using Spectre.Console;

namespace Umbraco.Cli.Infrastructure.Output;

public sealed class HumanOutputWriter : IOutputWriter
{
    public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null)
    {
        // For raw objects, pretty-print via Spectre markup.
        AnsiConsole.MarkupLine($"[green]✓[/] Done");
    }

    public void WriteError(
        int code,
        string message,
        string? category = null,
        string? serverVersion = null
    )
    {
        AnsiConsole.MarkupLine($"[red]✗ Error {code}:[/] {Markup.Escape(message)}");
        // Show the server version when it is known (#152): it is the single most useful bit of
        // triage context on a failure - which server produced it. Category is left to the
        // structured (JSON) output; the human line stays terse.
        if (!string.IsNullOrWhiteSpace(serverVersion))
            AnsiConsole.MarkupLine($"[grey]  Umbraco server:[/] {Markup.Escape(serverVersion)}");
    }

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

    public void WriteDryRun(string method, string url, string? body)
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
