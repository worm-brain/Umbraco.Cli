using System.Text;

namespace Umbraco.Cli.Commands.Content.Bulk;

/// <summary>
/// Reads the ids a bulk operation should act on (#85), one per line, from a file (<c>--file</c>)
/// or, when no file is given, from stdin — so a script can pipe ids in
/// (<c>umbraco content list --fields id | jq -r '.[]' | umbraco content bulk delete --yes</c>).
/// Blank lines and surrounding whitespace are ignored; validation of each id is left to the
/// executor so a malformed line is reported per-item rather than aborting the batch.
/// </summary>
public static class BulkIds
{
    /// <summary>
    /// Reads and trims non-empty id lines from <paramref name="file"/>, or from stdin when it is
    /// null. Any IO error propagates to the caller (the executor reports it via the output writer).
    /// </summary>
    /// <param name="file">The file to read ids from, or null to read stdin.</param>
    /// <returns>The trimmed, non-empty id lines in input order.</returns>
    public static IReadOnlyList<string> Read(FileInfo? file)
    {
        if (file is not null)
            return Parse(File.ReadLines(file.FullName));

        // Stdin is read as UTF-8 with any byte-order mark stripped, the same way --json-body -
        // reads it (JsonBodyInput). Console.In decodes with the console code page instead, so a
        // producer that writes a BOM (PowerShell, .NET's Encoding.UTF8) turned the first id into
        // garbage that failed as "not a valid GUID".
        using var reader = new StreamReader(
            Console.OpenStandardInput(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true
        );
        return Parse(reader);
    }

    /// <summary>Reads and trims the non-empty id lines from <paramref name="reader"/>.</summary>
    /// <param name="reader">The reader, e.g. over stdin.</param>
    /// <returns>The trimmed, non-empty id lines in input order.</returns>
    internal static IReadOnlyList<string> Parse(TextReader reader) => Parse(Lines(reader));

    private static IReadOnlyList<string> Parse(IEnumerable<string> lines) =>
        lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

    private static IEnumerable<string> Lines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }
}
