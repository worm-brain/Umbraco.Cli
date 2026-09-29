using BenchmarkDotNet.Attributes;
using Spectre.Console;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// Writing a large list result - <c>content list --all</c> on a big site - in each output
/// format, through the writers the commands use. Stdout is swapped for
/// <see cref="TextWriter.Null"/>, so what is measured is the serialisation and rendering, not the
/// terminal or pipe it would go to.
/// </summary>
[MemoryDiagnoser]
public class OutputBenchmarks
{
    // The list command's human columns and row projection (ContentListCommand).
    private static readonly string[] Headers = ["ID", "Name", "Published"];

    /// <summary>How many rows the list holds.</summary>
    private const int Rows = 2000;

    private List<ContentItemResponse> _items = null!;
    private List<object> _boxed = null!;
    private List<string[]> _cells = null!;
    private TextWriter _stdout = null!;
    private IAnsiConsole _terminal = null!;

    /// <summary>Builds the rows and points stdout and the human writer's console at nothing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _items = Fixtures.ContentItems(Rows);
        _boxed = [.. _items.Cast<object>()];
        _cells =
        [
            .. _items.Select(i => new[] { i.Id.ToString(), i.Name, i.IsPublished.ToString() }),
        ];
        _stdout = Console.Out;
        Console.SetOut(TextWriter.Null);

        // A fixed-width, colourless console: the rendered table does not depend on the terminal
        // the benchmark happens to run in.
        _terminal = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(TextWriter.Null),
            }
        );
        _terminal.Profile.Width = 160;
    }

    /// <summary>Restores stdout for BenchmarkDotNet's own output.</summary>
    [GlobalCleanup]
    public void Cleanup() => Console.SetOut(_stdout);

    /// <summary>The <c>-o json</c> envelope, serialised from the DTOs.</summary>
    [Benchmark]
    public void Json() => Write(new JsonOutputWriter());

    /// <summary>
    /// <c>-o json --fields id,name</c>: serialised to a DOM first so the projection can trim it,
    /// which is a different and costlier path than <see cref="Json"/>.
    /// </summary>
    [Benchmark]
    public void JsonWithFields() => Write(new JsonOutputWriter(["id", "name"]));

    /// <summary>The <c>-o csv</c> rows.</summary>
    [Benchmark]
    public void Csv() => Write(new CsvOutputWriter());

    /// <summary>The Spectre.Console table a terminal shows.</summary>
    [Benchmark]
    public void HumanTable() => Write(new HumanOutputWriter(_terminal));

    /// <summary>
    /// Writes the list as <c>CommandExecutor</c> hands it over for a complete list. The boxed items
    /// and table cells are built once in <see cref="Setup"/>, so only the writer is measured.
    /// </summary>
    /// <param name="writer">The writer under test.</param>
    private void Write(IOutputWriter writer) =>
        writer.WriteList(
            _boxed,
            Headers,
            _cells,
            ListPaging.Complete(_items.Count),
            "content.list",
            durationMs: 42
        );
}
