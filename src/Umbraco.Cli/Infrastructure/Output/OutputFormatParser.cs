namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Parses the raw <c>--output</c> option value into an <see cref="OutputFormat"/>.
/// Lives with the format it produces (not on any command) since every command
/// group needs it.
/// </summary>
public static class OutputFormatParser
{
    /// <summary>
    /// Returns the matching <see cref="OutputFormat"/> for <c>json</c> / <c>human</c> /
    /// <c>csv</c> (case-insensitive), or <c>null</c> when the value is unset (the caller then
    /// falls back to the TTY-based default) or unrecognised. An unrecognised value never reaches
    /// a command: the <c>--output</c> validator in <c>GlobalOptions</c> refuses it at parse time
    /// (#392), so <c>null</c> for one only matters to the parse-error reporter.
    /// </summary>
    /// <param name="value">The raw <c>--output</c> value, or null when the option was not given.</param>
    /// <returns>The format, or null for an unset or unrecognised value.</returns>
    public static OutputFormat? Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "human" => OutputFormat.Human,
            "csv" => OutputFormat.Csv,
            _ => null,
        };
}
