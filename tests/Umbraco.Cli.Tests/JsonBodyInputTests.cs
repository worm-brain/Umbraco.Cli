using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for reading a <c>--json-body</c> value from a file or stdin (#63). Sets
/// <see cref="Console.In"/>, which is process-global, so it shares the console-capture
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

    // The `-` (stdin) branch reads the raw standard-input stream as UTF-8 (not Console.In, which
    // Console.SetIn cannot redirect at the stream level), so it is covered end-to-end by the
    // integration harness (StdinBody_ContentCreate_ReadsPipedBody) with real piped bytes,
    // including a non-ASCII character to guard the UTF-8 decoding.
}
