namespace Umbraco.Cli.Tests;

/// <summary>
/// How many requests the <c>content bulk</c> commands make (#408). A bulk command is one write per
/// id by construction; what these pin is the reads each write adds on top.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class BulkRequestCountTests : IDisposable
{
    /// <summary>How many ids each bulk run is given.</summary>
    private const int Ids = 3;

    private readonly string _dir = Directory.CreateTempSubdirectory("umbraco-rc-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ContentBulkPublish_NoCulture_ReadsAndPublishesEachDocument()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk publish --file {ids}");

        // Assert: pinned as it is, not as it should be (#414). Each document is read on its
        // own to find its cultures, where one item/document read for all the ids would do.
        run.HasRequestCount(2 * Ids, $"1 document read + 1 publish per id ({Ids} ids)");
    }

    [Fact]
    public async Task ContentBulkPublish_NamedCulture_PublishesEachDocumentWithoutReadingIt()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk publish --file {ids} --culture en-US");

        // Assert
        run.HasRequestCount(Ids, $"1 publish per id ({Ids} ids); the cultures are given");
    }

    [Fact]
    public async Task ContentBulkUnpublish_NoCulture_ReadsAndUnpublishesEachDocument()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk unpublish --file {ids} --yes");

        // Assert: pinned as it is, not as it should be (#414), as for publish.
        run.HasRequestCount(2 * Ids, $"1 document read + 1 unpublish per id ({Ids} ids)");
    }

    [Fact]
    public async Task ContentBulkDelete_ThreeIds_DeletesEachDocumentOnce()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk delete --file {ids} --yes");

        // Assert
        run.HasRequestCount(Ids, $"1 delete per id ({Ids} ids)");
    }

    /// <summary>
    /// An instance holding <see cref="Ids"/> documents, and a file listing their ids one per line,
    /// as <c>--file</c> takes them.
    /// </summary>
    /// <returns>The CLI, and the path of the id file.</returns>
    private (HttpCli Cli, string IdFile) SiteWithDocuments()
    {
        var documents = new FakeTree();
        var ids = documents.AddMany(
            Ids,
            null,
            i => FakeUmbraco.DocumentItem($"Page {i}", Guid.NewGuid())
        );
        var file = Path.Combine(_dir, "ids.txt");
        File.WriteAllLines(file, ids.Select(id => id.ToString()));
        return (new HttpCli(FakeUmbraco.ContentSite(documents)), file);
    }
}
