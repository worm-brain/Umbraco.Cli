namespace Umbraco.Cli.Infrastructure.Output;

public static class OutputWriterFactory
{
    /// <summary>
    /// Returns a JSON writer when stdout is redirected (piped / AI agent),
    /// or when the user explicitly requested JSON. Falls back to human.
    /// </summary>
    /// <param name="requested">The explicitly requested format, or null to auto-detect.</param>
    /// <param name="fields">Optional field projection for JSON output (#63); ignored for human.</param>
    public static IOutputWriter Create(OutputFormat? requested = null, string[]? fields = null)
    {
        var format =
            requested ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Human);
        return format == OutputFormat.Json ? new JsonOutputWriter(fields) : new HumanOutputWriter();
    }
}
