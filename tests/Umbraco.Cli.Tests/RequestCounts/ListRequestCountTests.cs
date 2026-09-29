using static Umbraco.Cli.Tests.RequestCountAssert;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How many requests the <c>list</c> commands make (#408), for a fixed fixture and, where paging
/// makes the count depend on size, as a formula of it. These pin today's counts: a change that
/// lowers one updates the test with it, and one that raises one fails here first.
/// </summary>
[Collection("ConsoleCapture")]
public class ListRequestCountTests
{
    [Fact]
    public async Task ContentList_TenItemsOfThreeTypes_ReadsThePageAndEachTypeOnce()
    {
        // Arrange
        Guid[] types = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var documents = new FakeTree();
        documents.AddMany(10, null, i => FakeUmbraco.DocumentItem($"Page {i}", types[i % 3]));
        var cli = new HttpCli(FakeUmbraco.ContentSite(documents));

        // Act
        var run = await cli.RunAsync("content list");

        // Assert
        run.HasRequestCount(1 + 3, "1 tree page + 1 alias read per distinct document type");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(250)]
    public async Task ContentListAll_ItemsOfOneType_ReadsAPagePerHundredAndTheTypeOnce(int items)
    {
        // Arrange
        var type = Guid.NewGuid();
        var documents = new FakeTree();
        documents.AddMany(items, null, i => FakeUmbraco.DocumentItem($"Page {i}", type));
        var cli = new HttpCli(FakeUmbraco.ContentSite(documents));

        // Act
        var run = await cli.RunAsync("content list --all");

        // Assert
        run.HasRequestCount(
            PagesOf(items) + 1,
            $"ceil({items} / 100) tree pages + 1 alias read, cached across pages"
        );
    }

    [Fact]
    public async Task MediaList_TenItemsOfTwoTypes_ReadsThePageAndEachTypeOnce()
    {
        // Arrange
        Guid[] types = [Guid.NewGuid(), Guid.NewGuid()];
        var media = new FakeTree();
        media.AddMany(10, null, i => FakeUmbraco.MediaItem($"Image {i}", types[i % 2]));
        var handler = new RoutingHandler()
            .ServeTree("media", media)
            .ServeType("media-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync("media list");

        // Assert
        run.HasRequestCount(1 + 2, "1 tree page + 1 alias read per distinct media type");
    }

    [Fact]
    public async Task DataTypeList_FiveTypesOneFolder_WalksTheTreeAndReadsThePageInOneBatch()
    {
        // Arrange: three types at the root and two in a folder.
        var dataTypes = new FakeTree();
        dataTypes.AddMany(3, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var folder = Guid.NewGuid();
        dataTypes.Add(folder, null, FakeUmbraco.NamedItem("Folder", isFolder: true));
        dataTypes.AddMany(2, folder, i => FakeUmbraco.NamedItem($"Nested {i}"));
        var cli = new HttpCli(DataTypeSite(dataTypes));

        // Act
        var run = await cli.RunAsync("data-type list");

        // Assert: editorAlias and the configuration are only on the full body (#176), which the
        // batch read returns for up to 40 data types at a time (#418).
        run.HasRequestCount(2 + 1, "2 tree pages (root + folder) + 1 batch for the 5 data types");
    }

    [Fact]
    public async Task DataTypeList_TakeTwoOfFive_ReadsOnlyThePagesTypes()
    {
        // Arrange
        var dataTypes = new FakeTree();
        dataTypes.AddMany(5, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var cli = new HttpCli(DataTypeSite(dataTypes));

        // Act
        var run = await cli.RunAsync("data-type list --take 2");

        // Assert
        run.HasRequestCount(1 + 1, "1 tree page + 1 batch for the 2 data types on the page");
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(100, 2, 3)] // the walk stops at the first short page, so 100 costs an empty page
    [InlineData(250, 3, 3 + 3 + 2)] // list pages of 100, 100 and 50, each in batches of 40
    public async Task DataTypeListAll_ItemsAtTheRoot_WalksTheTreeOnceAndReadsEachPageInBatches(
        int items,
        int treePages,
        int batches
    )
    {
        // Arrange
        var dataTypes = new FakeTree();
        dataTypes.AddMany(items, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var cli = new HttpCli(DataTypeSite(dataTypes));

        // Act
        var run = await cli.RunAsync("data-type list --all");

        // Assert: one walk of the tree per run (#415), however many list pages --all reads, and
        // one batch per 40 data types on each page (#418).
        run.HasRequestCount(
            treePages + batches,
            $"{treePages} tree pages, walked once + {batches} batches of up to 40 per list page of 100"
        );
    }

    [Fact]
    public async Task DocumentTypeList_TakeFiveOfTwelve_ReadsOnlyThePagesAliases()
    {
        // Arrange
        var documentTypes = new FakeTree();
        documentTypes.AddMany(12, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler()
            .ServeTree("document-type", documentTypes)
            .ServeType("document-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync("document-type list --take 5");

        // Assert: the page is cut from the tree before any alias is read (#416), and its aliases
        // come in one batch (#418).
        run.HasRequestCount(1 + 1, "1 tree page + 1 batch for the 5 types listed");
    }

    [Theory]
    [InlineData("document-type")]
    [InlineData("media-type")]
    [InlineData("member-type")]
    public async Task TypeListAll_FiftyTypes_ReadsTheAliasesInBatchesOfForty(string kind)
    {
        // Arrange
        var types = new FakeTree();
        types.AddMany(50, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler()
            .ServeTree(kind, types)
            .ServeType(kind, FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync($"{kind} list --all");

        // Assert
        run.HasRequestCount(1 + 2, "1 tree page + 2 batches (40 + 10) for the 50 aliases");
    }

    [Fact]
    public async Task DocumentTypeGetByAlias_AliasInTheSecondBatch_StopsReadingOnceFound()
    {
        // Arrange: 90 types (one tree page); the alias asked for is the 50th's, in the second
        // batch of 40.
        var types = new FakeTree();
        var ids = types.AddMany(90, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler()
            .ServeTree("document-type", types)
            .ServeType("document-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync($"document-type get type{ids[49]:N}");

        // Assert
        run.HasRequestCount(
            1 + 2 + 1,
            "1 tree page + 2 batches of aliases (the third is never read) + 1 read of the type"
        );
    }

    [Fact]
    public async Task DataTypeList_ServerWithoutBatchEndpoints_ReadsEachTypeById()
    {
        // Arrange: Umbraco 17.0 to 17.2 have no batch endpoints; the path answers 404.
        var dataTypes = new FakeTree();
        dataTypes.AddMany(5, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler()
            .ServeTree("data-type", dataTypes)
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/batch"),
                System.Net.HttpStatusCode.NotFound,
                ""
            )
            .ServeById("data-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync("data-type list");

        // Assert
        run.HasRequestCount(1 + 1 + 5, "1 tree page + 1 batch that 404s + 1 read per data type");
    }

    [Fact]
    public async Task DictionaryList_OnePage_ReadsOnlyThePage()
    {
        // Arrange
        var dictionary = new FakeTree();
        dictionary.AddMany(10, null, i => FakeUmbraco.NamedItem($"Key {i}"));
        var cli = new HttpCli(new RoutingHandler().ServeTree("dictionary", dictionary).ElseEmpty());

        // Act
        var run = await cli.RunAsync("dictionary list");

        // Assert
        run.HasRequestCount(1, "1 tree page");
    }

    /// <summary>An instance serving <paramref name="dataTypes"/> as a tree, with a by-id body for each.</summary>
    /// <param name="dataTypes">The data-type tree.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler DataTypeSite(FakeTree dataTypes) =>
        new RoutingHandler()
            .ServeTree("data-type", dataTypes)
            .ServeType("data-type", FakeUmbraco.ContentType)
            .ElseEmpty();
}
