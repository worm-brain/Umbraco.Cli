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
    public static async Task<string> ReadAsync(string pathOrDash, CancellationToken ct) =>
        pathOrDash == StdinToken
            ? await Console.In.ReadToEndAsync(ct)
            : await File.ReadAllTextAsync(pathOrDash, ct);
}
