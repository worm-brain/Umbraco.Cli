namespace Umbraco.Cli.Infrastructure.Output;

public static class OutputWriterFactory
{
    /// <summary>
    /// Returns a JSON writer when stdout is redirected (piped / AI agent),
    /// or when the user explicitly requested JSON. Falls back to human.
    /// </summary>
    /// <param name="requested">The explicitly requested format, or null to auto-detect.</param>
    /// <param name="fields">Optional field/column projection for the structured formats (#63); ignored for human.</param>
    /// <param name="quiet">When true, wrap the writer so <c>--quiet</c> suppresses success chatter (#94).</param>
    public static IOutputWriter Create(
        OutputFormat? requested = null,
        string[]? fields = null,
        bool quiet = false
    )
    {
        // csv is only ever explicit; the TTY default remains json (piped) / human (terminal).
        var format =
            requested ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Human);
        IOutputWriter writer = format switch
        {
            OutputFormat.Json => new JsonOutputWriter(fields),
            OutputFormat.Csv => new CsvOutputWriter(fields),
            _ => new HumanOutputWriter(),
        };
        return quiet ? new QuietOutputWriter(writer) : writer;
    }
}
