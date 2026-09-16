using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Assembling a content snapshot from a live instance (#100): the exporter walks the tree, fetches
/// each document's raw body, preserves pre-order and captured parent, records the scope root, and
/// fails fast if any read fails (no partial snapshot).
/// </summary>
public class ContentExporterTests
{
    [Fact]
    public async Task ExportAsync_BuildsSnapshotPreservingOrderParentAndBody()
    {
        Guid root = Guid.NewGuid(),
            child = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTree.Add(new ContentTreeNode(root, null));
        fake.DocumentTree.Add(new ContentTreeNode(child, root));
        fake.DocumentRaw[root] = JsonNode.Parse($$"""{"id":"{{root}}","name":"Root"}""")!;
        fake.DocumentRaw[child] = JsonNode.Parse($$"""{"id":"{{child}}","name":"Child"}""")!;

        var result = await ContentExporter.ExportAsync(fake, root, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snap = result.Data!;
        Assert.Equal(root, snap.Root);
        Assert.Equal(2, snap.Documents.Count);
        // Pre-order preserved: parent before child.
        Assert.Equal(root, snap.Documents[0].Id);
        Assert.Null(snap.Documents[0].Parent);
        Assert.Equal(child, snap.Documents[1].Id);
        Assert.Equal(root, snap.Documents[1].Parent);
        // Verbatim body carried.
        Assert.Equal("Child", (string?)snap.Documents[1].Body["name"]);
    }

    [Fact]
    public async Task ExportAsync_ReadFailure_FailsFastWithNoPartialSnapshot()
    {
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTree.Add(new ContentTreeNode(id, null));
        // DocumentRaw left empty -> the per-document read 404s.

        var result = await ContentExporter.ExportAsync(fake, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
