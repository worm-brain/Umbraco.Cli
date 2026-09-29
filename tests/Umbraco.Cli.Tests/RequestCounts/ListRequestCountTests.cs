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
            .ServeById("media-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync("media list");

        // Assert
        run.HasRequestCount(1 + 2, "1 tree page + 1 alias read per distinct media type");
    }

    [Fact]
    public async Task DataTypeList_FiveTypesOneFolder_WalksTheTreeAndReadsEachTypeOnThePage()
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

        // Assert: the by-id read per item is deliberate (#176, and the command's help says so):
        // editorAlias and the configuration are only on the by-id body.
        run.HasRequestCount(2 + 5, "2 tree pages (root + folder) + 1 read per data type listed");
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
        run.HasRequestCount(1 + 2, "1 tree page + 1 read per data type on the page (--take 2)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(250)]
    public async Task DataTypeListAll_ItemsAtTheRoot_WalksTheWholeTreeForEveryPage(int items)
    {
        // Arrange
        var dataTypes = new FakeTree();
        dataTypes.AddMany(items, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var cli = new HttpCli(DataTypeSite(dataTypes));

        // Act
        var run = await cli.RunAsync("data-type list --all");

        // Assert: pinned as it is, not as it should be (#415). Each page of --all walks the whole
        // data-type tree again, so the walk is repeated ceil(n / 100) times and the tree requests
        // grow with the square of the size. The walk stops at the first short page, so an exact
        // multiple of 100 costs one extra, empty, tree page.
        run.HasRequestCount(
            PagesOf(items) * (items / PageSize + 1) + items,
            $"ceil({items} / 100) list pages x ({items} / 100 + 1) tree pages each + {items} reads"
        );
    }

    [Fact]
    public async Task DocumentTypeList_TakeFiveOfTwelve_ReadsEveryTypesAlias()
    {
        // Arrange
        var documentTypes = new FakeTree();
        documentTypes.AddMany(12, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler()
            .ServeTree("document-type", documentTypes)
            .ServeById("document-type", FakeUmbraco.ContentType)
            .ElseEmpty();
        var cli = new HttpCli(handler);

        // Act
        var run = await cli.RunAsync("document-type list --take 5");

        // Assert: pinned as it is, not as it should be (#416). The page is cut client-side after
        // every type's alias has been read, so --take bounds the output but not the reads; media
        // and member types share the code.
        run.HasRequestCount(
            1 + 12,
            "1 tree page + 1 alias read per type in the tree, not per type listed"
        );
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
            .ServeById("data-type", FakeUmbraco.ContentType)
            .ElseEmpty();
}
