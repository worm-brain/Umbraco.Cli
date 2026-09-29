using static Umbraco.Cli.Tests.RequestCountAssert;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How many requests the <c>tree</c> commands make (#408). The Management API has no
/// "descendants" read, so a walk costs one request per level page: the start level, and each node
/// with children that the depth budget lets the walk descend into.
/// </summary>
[Collection("ConsoleCapture")]
public class TreeRequestCountTests
{
    [Fact]
    public async Task ContentTree_DefaultDepth_ReadsOnlyTheFirstLevel()
    {
        // Arrange
        var cli = new HttpCli(FakeUmbraco.ContentSite(ThreeLevelTree()));

        // Act
        var run = await cli.RunAsync("content tree");

        // Assert
        run.HasRequestCount(1, "1 page of the root level; children are not descended into");
    }

    [Fact]
    public async Task ContentTreeRecursive_TwoNodesWithChildren_ReadsTheRootAndEachParentsChildren()
    {
        // Arrange
        var cli = new HttpCli(FakeUmbraco.ContentSite(ThreeLevelTree()));

        // Act
        var run = await cli.RunAsync("content tree --recursive");

        // Assert
        run.HasRequestCount(
            1 + ThreeLevelTreeParents,
            "1 root level + 1 level per node with children"
        );
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public async Task ContentTree_WideLevel_ReadsAPagePerHundred(int items)
    {
        // Arrange
        var documents = new FakeTree();
        documents.AddMany(items, null, i => FakeUmbraco.DocumentItem($"Page {i}", Guid.NewGuid()));
        var cli = new HttpCli(FakeUmbraco.ContentSite(documents));

        // Act
        var run = await cli.RunAsync("content tree");

        // Assert
        run.HasRequestCount(PagesOf(items), $"ceil({items} / 100) pages of the root level");
    }

    [Fact]
    public async Task MediaTreeRecursive_TwoNodesWithChildren_ReadsTheRootAndEachParentsChildren()
    {
        // Arrange: three at the root, two under the first, one under the first of those.
        var type = Guid.NewGuid();
        var media = new FakeTree();
        var roots = media.AddMany(3, null, i => FakeUmbraco.MediaItem($"Folder {i}", type));
        var children = media.AddMany(2, roots[0], i => FakeUmbraco.MediaItem($"Image {i}", type));
        media.AddMany(1, children[0], i => FakeUmbraco.MediaItem($"Crop {i}", type));
        var cli = new HttpCli(new RoutingHandler().ServeTree("media", media).ElseEmpty());

        // Act
        var run = await cli.RunAsync("media tree --recursive");

        // Assert
        run.HasRequestCount(1 + 2, "1 root level + 1 level per node with children");
    }

    [Fact]
    public async Task DictionaryTreeRecursive_TwoNodesWithChildren_ReadsTheRootAndEachParentsChildren()
    {
        // Arrange: three at the root, two under the second, one under the first of those.
        var dictionary = new FakeTree();
        var roots = dictionary.AddMany(3, null, i => FakeUmbraco.NamedItem($"Section {i}"));
        var children = dictionary.AddMany(2, roots[1], i => FakeUmbraco.NamedItem($"Key {i}"));
        dictionary.AddMany(1, children[0], i => FakeUmbraco.NamedItem($"Sub {i}"));
        var cli = new HttpCli(new RoutingHandler().ServeTree("dictionary", dictionary).ElseEmpty());

        // Act
        var run = await cli.RunAsync("dictionary tree --recursive");

        // Assert
        run.HasRequestCount(1 + 2, "1 root level + 1 level per node with children");
    }

    /// <summary>How many nodes of <see cref="ThreeLevelTree"/> have children.</summary>
    private const int ThreeLevelTreeParents = 2;

    /// <summary>
    /// Three documents at the root, two under the first, and one under the first of those: three
    /// levels deep, with <see cref="ThreeLevelTreeParents"/> nodes that have children.
    /// </summary>
    /// <returns>The tree.</returns>
    private static FakeTree ThreeLevelTree()
    {
        var type = Guid.NewGuid();
        var documents = new FakeTree();
        var roots = documents.AddMany(3, null, i => FakeUmbraco.DocumentItem($"Root {i}", type));
        var children = documents.AddMany(
            2,
            roots[0],
            i => FakeUmbraco.DocumentItem($"Child {i}", type)
        );
        documents.AddMany(1, children[0], i => FakeUmbraco.DocumentItem($"Grandchild {i}", type));
        return documents;
    }
}
