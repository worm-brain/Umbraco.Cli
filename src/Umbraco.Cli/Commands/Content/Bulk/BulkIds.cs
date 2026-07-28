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
        var lines = file is not null ? File.ReadLines(file.FullName) : ReadStdinLines();
        return lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    /// <summary>Yields lines from stdin until end-of-input.</summary>
    /// <returns>The raw stdin lines.</returns>
    private static IEnumerable<string> ReadStdinLines()
    {
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
            yield return line;
    }
}
