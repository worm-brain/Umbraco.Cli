using System.Text.Json.Nodes;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>The JSON helpers shared by the content and media body normalisers (#274).</summary>
public class SnapshotBodyTests
{
    [Fact]
    public void Sorted_ByAliasThenCulture_OrdersOrdinallyWithNullFirst()
    {
        // Arrange
        var array = JsonNode
            .Parse(
                """[{"a":"b","c":"en"},{"a":"b","c":null},{"a":"B","c":"da"},{"a":"a","c":"en"}]"""
            )!
            .AsArray();

        // Act
        var sorted = SnapshotBody.Sorted(
            array,
            v => (SnapshotBody.Text(v, "a"), SnapshotBody.Text(v, "c"), null)
        );

        // Assert: ordinal puts "B" before "a"; the null culture sorts before "en".
        Assert.Equal(
            ["B/da", "a/en", "b/", "b/en"],
            sorted.Select(v => $"{SnapshotBody.Text(v, "a")}/{SnapshotBody.Text(v, "c")}")
        );
    }

    [Fact]
    public void Text_StringProperty_ReturnsIt()
    {
        // Act
        var text = SnapshotBody.Text(JsonNode.Parse("""{"alias":"title"}"""), "alias");

        // Assert
        Assert.Equal("title", text);
    }

    [Fact]
    public void Text_NonStringOrMissingProperty_ReturnsNull()
    {
        // Arrange
        var node = JsonNode.Parse("""{"n":1}""");

        // Act
        var texts = new[] { SnapshotBody.Text(node, "n"), SnapshotBody.Text(node, "missing") };

        // Assert
        Assert.All(texts, Assert.Null);
    }
}
