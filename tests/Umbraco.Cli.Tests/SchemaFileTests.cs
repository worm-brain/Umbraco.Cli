using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The snapshot argument of <c>schema diff</c> and <c>schema apply</c>: a snapshot piped to stdin
/// (<c>-</c>) loads the same as the file named by path (#443). The stdin tests redirect
/// <see cref="Umbraco.Cli.Infrastructure.StandardInput"/>, which is process-global, so the class
/// shares the console-capture collection.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class SchemaFileTests : IDisposable
{
    /// <summary>A snapshot holding one dictionary item with a Danish translation, so decoding shows.</summary>
    private const string Snapshot = """
        {
          "schemaVersion": "4",
          "dictionaryItems": [
            {
              "name": "Common.Search",
              "translations": [ { "isoCode": "da-DK", "translation": "Søg på æøå" } ]
            }
          ]
        }
        """;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-schema-file-{Guid.NewGuid():N}"
    );

    public SchemaFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // PowerShell and .NET's Encoding.UTF8 write a BOM
    public async Task LoadAsync_StdinUtf8NonAscii_LoadsTheSameAsTheFile(bool byteOrderMark)
    {
        // Arrange: the same bytes saved to a file and piped to stdin.
        var bytes = RedirectedStdin.Utf8(Snapshot, byteOrderMark);
        var path = Path.Combine(_dir, "schema.json");
        await File.WriteAllBytesAsync(path, bytes);
        var fromFile = await SchemaFile.LoadAsync(path, CancellationToken.None);
        using var stdin = new RedirectedStdin(bytes);

        // Act
        var fromStdin = await SchemaFile.LoadAsync("-", CancellationToken.None);

        // Assert: compared as serialized, so every name and translation takes part.
        Assert.Equal(fromFile.ToJson(), fromStdin.ToJson());
    }
}
