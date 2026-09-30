using System.Text.Json;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Loads a <see cref="ContentSnapshot"/> from the snapshot argument shared by <c>content diff</c>
/// and <c>content apply</c> (issue #100). A path of <c>-</c> reads stdin, so a snapshot can be
/// piped straight from <c>content export</c>. Parse/IO failures surface as exceptions, which the
/// executor turns into a clean error + non-zero exit.
/// </summary>
public static class ContentFile
{
    /// <summary>
    /// Reads and parses a snapshot from a file path, or from stdin when the path is <c>-</c>. Both
    /// are decoded as UTF-8 with a leading BOM dropped (stdin through <see cref="StandardInput"/>),
    /// so a piped snapshot with non-ASCII text reads the same as the file named by path (#443).
    /// </summary>
    /// <param name="path">The snapshot file path, or <c>-</c> for stdin.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed snapshot.</returns>
    /// <exception cref="IOException">The file or stdin could not be read, e.g. the file does not exist.</exception>
    /// <exception cref="JsonException">The text is not a supported content snapshot.</exception>
    public static async Task<ContentSnapshot> LoadAsync(string path, CancellationToken ct)
    {
        var text =
            path == "-"
                ? await StandardInput.ReadToEndAsync(ct)
                : await File.ReadAllTextAsync(path, ct);
        return ContentSnapshot.FromJson(text);
    }
}
