namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Parses the raw <c>--output</c> option value into an <see cref="OutputFormat"/>.
/// Lives with the format it produces (not on any command) since every command
/// group needs it.
/// </summary>
public static class OutputFormatParser
{
    /// <summary>
    /// Returns the matching <see cref="OutputFormat"/> for <c>json</c> / <c>human</c>
    /// (case-insensitive), or <c>null</c> when the value is unset or unrecognised —
    /// in which case the caller falls back to the TTY-based default.
    /// </summary>
    public static OutputFormat? Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "human" => OutputFormat.Human,
            "csv" => OutputFormat.Csv,
            _ => null,
        };
}
