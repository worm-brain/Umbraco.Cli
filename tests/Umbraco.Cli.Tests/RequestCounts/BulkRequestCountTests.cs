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
    public async Task ContentBulkPublish_NoCulture_ReadsTheCulturesInOneBatch()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk publish --file {ids}");

        // Assert (#414)
        run.HasRequestCount(1 + Ids, $"1 item/document read + 1 publish per id ({Ids} ids)");
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
    public async Task ContentBulkUnpublish_NoCulture_ReadsTheCulturesInOneBatch()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments();

        // Act
        var run = await cli.RunAsync($"content bulk unpublish --file {ids} --yes");

        // Assert (#414)
        run.HasRequestCount(1 + Ids, $"1 item/document read + 1 unpublish per id ({Ids} ids)");
    }

    [Fact]
    public async Task ContentBulkPublish_FiftyIdsNoCulture_ReadsTheCulturesInBatchesOfForty()
    {
        // Arrange
        var (cli, ids) = SiteWithDocuments(50);

        // Act
        var run = await cli.RunAsync($"content bulk publish --file {ids}");

        // Assert (#414): 40 ids per read keeps the query string under IIS's 2 KB limit.
        run.HasRequestCount(2 + 50, "ceil(50 / 40) = 2 item/document reads + 1 publish per id");
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
    /// An instance holding <paramref name="count"/> documents, and a file listing their ids one per line,
    /// as <c>--file</c> takes them.
    /// </summary>
    /// <param name="count">How many documents.</param>
    /// <returns>The CLI, and the path of the id file.</returns>
    private (HttpCli Cli, string IdFile) SiteWithDocuments(int count = Ids)
    {
        var documents = new FakeTree();
        var ids = documents.AddMany(
            count,
            null,
            i => FakeUmbraco.DocumentItem($"Page {i}", Guid.NewGuid())
        );
        var file = Path.Combine(_dir, "ids.txt");
        File.WriteAllLines(file, ids.Select(id => id.ToString()));
        return (new HttpCli(FakeUmbraco.ContentSite(documents)), file);
    }
}
