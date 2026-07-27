using System.Text;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Reads a <c>--json-body</c> value that is either a file path or <c>-</c> for stdin (#63), so
/// a body can be piped in cleanly (<c>cat body.json | umbraco content create --json-body -</c>).
/// </summary>
public static class JsonBodyInput
{
    /// <summary>The sentinel that means "read the body from standard input".</summary>
    public const string StdinToken = "-";

    /// <summary>
    /// Reads the JSON body from stdin when <paramref name="pathOrDash"/> is <c>-</c>, otherwise
    /// from the file at that path.
    /// </summary>
    /// <param name="pathOrDash">A file path, or <c>-</c> for stdin.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body text.</returns>
    public static async Task<string> ReadAsync(string pathOrDash, CancellationToken ct)
    {
        if (pathOrDash != StdinToken)
            return await File.ReadAllTextAsync(pathOrDash, ct);

        // Read stdin as UTF-8 (stripping a BOM) rather than via Console.In, which decodes with
        // the console input code page (often not UTF-8 on Windows) and would mangle non-ASCII
        // JSON or choke on a BOM. This matches File.ReadAllTextAsync's UTF-8 default.
        using var reader = new StreamReader(
            Console.OpenStandardInput(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true
        );
        return await reader.ReadToEndAsync(ct);
    }
}
