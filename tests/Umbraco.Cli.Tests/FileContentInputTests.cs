using System.CommandLine;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>--content</c> / <c>--content-file</c>: one source or the other. Two sources for one value is
/// an input error, never a silent precedence (docs/conventions.md 4.5). The stdin test redirects
/// <see cref="Umbraco.Cli.Infrastructure.StandardInput"/>, which is process-global, so the class
/// shares the console-capture collection.
/// </summary>
[Collection("ConsoleCapture")]
public class FileContentInputTests
{
    private static (ParseResult Parse, Option<string?> Content, Option<FileInfo?> File) Parse(
        string args
    )
    {
        var (content, file) = FileContentInput.Options();
        var cmd = new RootCommand { content, file };
        return (cmd.Parse(args), content, file);
    }

    [Fact]
    public async Task ReadAsync_BothGiven_IsAnInputError()
    {
        var path = Path.GetTempFileName();
        var (parse, content, file) = Parse($"--content inline --content-file {path}");

        await Assert.ThrowsAsync<InvalidInputException>(() =>
            FileContentInput.ReadAsync(parse, content, file, CancellationToken.None)
        );
        File.Delete(path);
    }

    [Fact]
    public async Task ReadAsync_FileGiven_ReadsTheFile()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "<p>hello</p>");
        var (parse, content, file) = Parse($"--content-file {path}");

        var read = await FileContentInput.ReadAsync(parse, content, file, CancellationToken.None);

        Assert.Equal("<p>hello</p>", read);
        File.Delete(path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // PowerShell and .NET's Encoding.UTF8 write a BOM
    public async Task ReadAsync_ContentFileDashWithUtf8NonAscii_ReadsTheSameAsTheFile(
        bool byteOrderMark
    )
    {
        // Arrange: a Danish template, saved to a file and piped to stdin as the same bytes (#443).
        var bytes = RedirectedStdin.Utf8("<h1>Søg</h1>\n<p>æøå</p>\n", byteOrderMark);
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var (byPath, pathContent, pathFile) = Parse($"--content-file {path}");
            var fromFile = await FileContentInput.ReadAsync(
                byPath,
                pathContent,
                pathFile,
                CancellationToken.None
            );
            var (byDash, content, file) = Parse("--content-file -");
            using var stdin = new RedirectedStdin(bytes);

            // Act
            var fromStdin = await FileContentInput.ReadAsync(
                byDash,
                content,
                file,
                CancellationToken.None
            );

            // Assert
            Assert.Equal(fromFile, fromStdin);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_NeitherGiven_IsNull()
    {
        var (parse, content, file) = Parse("");

        var read = await FileContentInput.ReadAsync(parse, content, file, CancellationToken.None);

        Assert.Null(read);
    }
}
