using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for reading a <c>--json-body</c> value from a file or stdin (#63). The stdin tests redirect
/// <see cref="StandardInput"/>, which is process-global, so the class shares the console-capture
/// collection.
/// </summary>
[Collection("ConsoleCapture")]
public class JsonBodyInputTests
{
    [Fact]
    public async Task ReadAsync_FilePath_ReadsFileContents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umbraco-body-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, """{"a":1}""");
        try
        {
            var body = await JsonBodyInput.ReadAsync(path, CancellationToken.None);
            Assert.Equal("""{"a":1}""", body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // PowerShell and .NET's Encoding.UTF8 write a BOM
    public async Task ReadAsync_StdinUtf8NonAscii_ReadsTheSameAsTheFile(bool byteOrderMark)
    {
        // Arrange: the same bytes saved to a file and piped to stdin (#443).
        var bytes = RedirectedStdin.Utf8("""{"name":"Søg æøå"}""", byteOrderMark);
        var path = Path.Combine(Path.GetTempPath(), $"umbraco-body-{Guid.NewGuid()}.json");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var fromFile = await JsonBodyInput.ReadAsync(path, CancellationToken.None);
            using var stdin = new RedirectedStdin(bytes);

            // Act
            var fromStdin = await JsonBodyInput.ReadAsync("-", CancellationToken.None);

            // Assert
            Assert.Equal(fromFile, fromStdin);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
