using System.CommandLine;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The shared <c>--content</c> / <c>--content-file</c> option pair used by commands that take a
/// text body (templates, scripts, stylesheets, partial views). Centralises the two options and the
/// "file wins over inline, read inside the executor" resolution so each command does not
/// re-implement it.
/// </summary>
public static class FileContentInput
{
    /// <summary>Builds the <c>--content</c> / <c>--content-file</c> option pair.</summary>
    /// <param name="inlineDescription">Help text for the inline <c>--content</c> option.</param>
    /// <param name="fileDescription">Help text for the <c>--content-file</c> option.</param>
    /// <returns>The two options, to be added to a command.</returns>
    public static (Option<string?> Content, Option<FileInfo?> ContentFile) Options(
        string inlineDescription = "Inline content. Mutually exclusive with --content-file.",
        string fileDescription =
            "Read the content from this local file (takes precedence over --content)."
    )
    {
        var content = new Option<string?>("--content") { Description = inlineDescription };
        var contentFile = new Option<FileInfo?>("--content-file") { Description = fileDescription };
        return (content, contentFile);
    }

    /// <summary>
    /// Resolves the content to send: the <c>--content-file</c> body if given (read here, inside the
    /// executor's try, so a missing file surfaces as a clean error), else <c>--content</c>, else
    /// null. Callers that require content treat null as an error; callers with a default coalesce it.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="content">The inline-content option.</param>
    /// <param name="contentFile">The content-file option.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved content, or null when neither option was supplied.</returns>
    public static async Task<string?> ReadAsync(
        ParseResult parseResult,
        Option<string?> content,
        Option<FileInfo?> contentFile,
        CancellationToken ct
    )
    {
        var file = parseResult.GetValue(contentFile);
        if (file is not null)
            return await File.ReadAllTextAsync(file.FullName, ct);
        return parseResult.GetValue(content);
    }
}
