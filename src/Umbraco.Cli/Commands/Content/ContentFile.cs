namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Loads a <see cref="ContentSnapshot"/> from the snapshot argument shared by <c>content diff</c>
/// and <c>content apply</c> (issue #100). A path of <c>-</c> reads stdin, so a snapshot can be
/// piped straight from <c>content export</c>. Parse/IO failures surface as exceptions, which the
/// executor turns into a clean error + non-zero exit.
/// </summary>
public static class ContentFile
{
    /// <summary>Reads and parses a snapshot from a file path, or from stdin when the path is <c>-</c>.</summary>
    /// <param name="path">The snapshot file path, or <c>-</c> for stdin.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed snapshot.</returns>
    public static async Task<ContentSnapshot> LoadAsync(string path, CancellationToken ct)
    {
        var text =
            path == "-"
                ? await Console.In.ReadToEndAsync(ct)
                : await File.ReadAllTextAsync(path, ct);
        return ContentSnapshot.FromJson(text);
    }
}
