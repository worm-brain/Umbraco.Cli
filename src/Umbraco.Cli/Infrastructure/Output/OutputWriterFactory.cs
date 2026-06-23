namespace Umbraco.Cli.Infrastructure.Output;

public static class OutputWriterFactory
{
    /// <summary>
    /// Returns a JSON writer when stdout is redirected (piped / AI agent),
    /// or when the user explicitly requested JSON. Falls back to human.
    /// </summary>
    public static IOutputWriter Create(OutputFormat? requested = null)
    {
        var format = requested ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Human);
        return format == OutputFormat.Json ? new JsonOutputWriter() : new HumanOutputWriter();
    }
}
