using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The snapshot argument of <c>content diff</c> and <c>content apply</c>: a snapshot piped to
/// stdin (<c>-</c>) loads the same as the file named by path (#443). The stdin tests redirect
/// <see cref="Umbraco.Cli.Infrastructure.StandardInput"/>, which is process-global, so the class
/// shares the console-capture collection.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class ContentFileTests : IDisposable
{
    /// <summary>A one-document snapshot whose name and value are Danish, so decoding shows.</summary>
    private const string Snapshot = """
        {
          "contentVersion": "1",
          "documents": [
            {
              "id": "3f7a8b2e-1234-5678-abcd-ef0123456789",
              "body": {
                "variants": [ { "culture": "da-DK", "name": "Søg" } ],
                "values": [ { "alias": "title", "culture": "da-DK", "value": "Blåbærgrød" } ]
              }
            }
          ]
        }
        """;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-content-file-{Guid.NewGuid():N}"
    );

    public ContentFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // PowerShell and .NET's Encoding.UTF8 write a BOM
    public async Task LoadAsync_StdinUtf8NonAscii_LoadsTheSameAsTheFile(bool byteOrderMark)
    {
        // Arrange: the same bytes saved to a file and piped to stdin.
        var bytes = RedirectedStdin.Utf8(Snapshot, byteOrderMark);
        var path = Path.Combine(_dir, "content.json");
        await File.WriteAllBytesAsync(path, bytes);
        var fromFile = await ContentFile.LoadAsync(path, CancellationToken.None);
        using var stdin = new RedirectedStdin(bytes);

        // Act
        var fromStdin = await ContentFile.LoadAsync("-", CancellationToken.None);

        // Assert: compared as serialized, so every name and value takes part.
        Assert.Equal(fromFile.ToJson(), fromStdin.ToJson());
    }
}
