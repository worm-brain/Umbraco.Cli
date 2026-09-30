using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The shared <c>--content</c> / <c>--content-file</c> option pair used by commands that take a
/// text body (templates, scripts, stylesheets, partial views). Centralises the two options and
/// their resolution: one or the other, never both (docs/conventions.md 4.5), with <c>-</c> reading
/// the file from stdin.
/// </summary>
public static class FileContentInput
{
    /// <summary>Builds the <c>--content</c> / <c>--content-file</c> option pair.</summary>
    /// <param name="inlineDescription">Help text for the inline <c>--content</c> option.</param>
    /// <param name="fileDescription">Help text for the <c>--content-file</c> option.</param>
    /// <returns>The two options, to be added to a command.</returns>
    public static (Option<string?> Content, Option<FileInfo?> ContentFile) Options(
        string inlineDescription = "Inline content. Give this or --content-file, not both.",
        string fileDescription = "Read the content from this local file, or - for stdin."
    )
    {
        var content = new Option<string?>("--content") { Description = inlineDescription };
        var contentFile = new Option<FileInfo?>("--content-file") { Description = fileDescription };
        return (content, contentFile);
    }

    /// <summary>
    /// Resolves the content to send: the <c>--content-file</c> body (read here, inside the executor's
    /// try, so a missing file surfaces as a clean error; <c>-</c> reads stdin), or <c>--content</c>,
    /// or null. Callers that require content treat null as an error; callers with a default coalesce it.
    /// A file and stdin are both decoded as UTF-8 with a leading BOM dropped (stdin through
    /// <see cref="StandardInput"/>), so a piped template reads the same as the file named by path.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="content">The inline-content option.</param>
    /// <param name="contentFile">The content-file option.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved content, or null when neither option was supplied.</returns>
    /// <exception cref="InvalidInputException">Both options were given.</exception>
    /// <exception cref="IOException">The file or stdin could not be read, e.g. the file does not exist.</exception>
    public static async Task<string?> ReadAsync(
        ParseResult parseResult,
        Option<string?> content,
        Option<FileInfo?> contentFile,
        CancellationToken ct
    )
    {
        var file = parseResult.GetValue(contentFile);
        var inline = parseResult.GetValue(content);
        // Two sources for one value: refuse rather than silently pick one.
        if (file is not null && inline is not null)
            throw new InvalidInputException(
                $"Give {content.Name} or {contentFile.Name}, not both."
            );
        if (file is null)
            return inline;
        // FileInfo keeps the path as typed, so '-' is recognisable before it is resolved.
        return file.ToString() == "-"
            ? await StandardInput.ReadToEndAsync(ct)
            : await File.ReadAllTextAsync(file.FullName, ct);
    }
}
