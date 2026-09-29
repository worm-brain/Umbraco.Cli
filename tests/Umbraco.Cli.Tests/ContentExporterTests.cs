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

    [Fact]
    public async Task ExportAsync_FirstReadFinishesLast_KeepsTheTreeOrder()
    {
        // Arrange (#422): the first document's read waits until the last one's has run.
        Guid first = Guid.NewGuid(),
            middle = Guid.NewGuid(),
            last = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        foreach (var id in new[] { first, middle, last })
        {
            fake.DocumentTree.Add(new ContentTreeNode(id, null));
            fake.DocumentRaw[id] = JsonNode.Parse($$"""{"id":"{{id}}"}""")!;
        }
        var lastRead = new TaskCompletionSource();
        fake.BeforeDocumentRawRead = id =>
        {
            if (id == last)
                lastRead.SetResult();
            return id == first ? lastRead.Task : Task.CompletedTask;
        };

        // Act
        var result = await ContentExporter.ExportAsync(fake, null, CancellationToken.None);

        // Assert
        Assert.Equal([first, middle, last], result.Data!.Documents.Select(d => d.Id));
    }

    [Fact]
    public async Task ExportAsync_TwoReadsFailLaterOneFirst_ReturnsTheEarlierDocumentsFailure()
    {
        // Arrange (#422): neither body exists, and the second read fails before the first.
        Guid first = Guid.NewGuid(),
            second = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTree.Add(new ContentTreeNode(first, null));
        fake.DocumentTree.Add(new ContentTreeNode(second, null));
        var secondRead = new TaskCompletionSource();
        fake.BeforeDocumentRawRead = id =>
        {
            if (id == second)
                secondRead.SetResult();
            return id == first ? secondRead.Task : Task.CompletedTask;
        };

        // Act
        var result = await ContentExporter.ExportAsync(fake, null, CancellationToken.None);

        // Assert: the failure a one-at-a-time export would have stopped at.
        Assert.Equal($"Not found: {first}", result.ErrorMessage);
    }
}
