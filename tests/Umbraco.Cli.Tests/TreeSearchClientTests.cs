using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the content/media tree-walk and find client methods (#89): the tree walk descends
/// the tree/{document,media} root/children endpoints to the requested depth (and no further),
/// tagging each node with depth and parent; find-by-name defers to the server search endpoint; and
/// find-by-path walks a name segment at a time. Drives the real client against
/// <see cref="RoutingHandler"/> so the multi-step walks return a distinct body per level.
/// </summary>
public class TreeSearchClientTests
{
    private static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    private static bool Has(HttpRequestMessage r, string fragment) =>
        r.RequestUri!.AbsoluteUri.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // ── Content tree ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContentTreeAsync_DepthOne_ListsDirectChildrenWithoutDescending()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "tree/document/root"),
            HttpStatusCode.OK,
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":true,"variants":[{"name":"Home"}]}]}"""
        );
        var client = Client(handler);

        var result = await client.GetContentTreeAsync(null, maxDepth: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var node = Assert.Single(result.Data!);
        Assert.Equal("Home", node.Name);
        Assert.Equal(1, node.Depth);
        Assert.True(node.HasChildren);
        Assert.Null(node.ParentId);
        // Depth 1 means the child level is NOT walked even though the node has children.
        Assert.DoesNotContain(
            handler.Requests,
            u => u.AbsoluteUri.Contains("tree/document/children")
        );
    }

    [Fact]
    public async Task GetContentTreeAsync_Recursive_DescendsTaggingDepthAndParent()
    {
        var a = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document/root"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":true,"variants":[{"name":"Home"}]}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document/children"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"9c4d5e6f-1234-5678-abcd-ef0123456789","hasChildren":false,"variants":[{"name":"About"}]}]}"""
            );
        var client = Client(handler);

        var result = await client.GetContentTreeAsync(null, maxDepth: 5, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Data!,
            root =>
            {
                Assert.Equal("Home", root.Name);
                Assert.Equal(1, root.Depth);
                Assert.Null(root.ParentId);
            },
            child =>
            {
                Assert.Equal("About", child.Name);
                Assert.Equal(2, child.Depth);
                Assert.Equal(a, child.ParentId); // parent is the node we descended from
            }
        );
    }

    // ── Content find ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindContentByNameAsync_HitsSearchEndpointAndMaps()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "item/document/search"),
            HttpStatusCode.OK,
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","variants":[{"name":"About"}]}]}"""
        );
        var client = Client(handler);

        var result = await client.FindContentByNameAsync(
            "Abo",
            null,
            0,
            20,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(handler.Requests, u => u.AbsoluteUri.Contains("query=Abo"));
        Assert.Equal("About", Assert.Single(result.Data!.Items).Name);
    }

    [Fact]
    public async Task FindContentByPathAsync_WalksSegmentsAndReturnsLeaf()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document/root"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":true,"variants":[{"name":"Home"}]}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document/children"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"9c4d5e6f-1234-5678-abcd-ef0123456789","hasChildren":false,"variants":[{"name":"About"}]}]}"""
            );
        var client = Client(handler);

        var result = await client.FindContentByPathAsync("Home/About", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("About", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task FindContentByPathAsync_NoMatch_ReturnsEmpty()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "tree/document/root"),
            HttpStatusCode.OK,
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":false,"variants":[{"name":"Home"}]}]}"""
        );
        var client = Client(handler);

        var result = await client.FindContentByPathAsync("Nope", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    // ── Media parity ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMediaTreeAsync_ReadsMediaTreeRoot()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "tree/media/root"),
            HttpStatusCode.OK,
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":false,"variants":[{"name":"Images"}]}]}"""
        );
        var client = Client(handler);

        var result = await client.GetMediaTreeAsync(null, maxDepth: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Images", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task FindMediaByNameAsync_HitsMediaSearchEndpoint()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "item/media/search"),
            HttpStatusCode.OK,
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","variants":[{"name":"logo"}]}]}"""
        );
        var client = Client(handler);

        var result = await client.FindMediaByNameAsync("log", null, 0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(handler.Requests, u => u.AbsoluteUri.Contains("query=log"));
        Assert.Equal("logo", Assert.Single(result.Data!.Items).Name);
    }

    [Fact]
    public async Task FindMediaByPathAsync_WalksSegmentsAndReturnsLeaf()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/media/root"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","hasChildren":true,"variants":[{"name":"Images"}]}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/media/children"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"9c4d5e6f-1234-5678-abcd-ef0123456789","hasChildren":false,"variants":[{"name":"Logos"}]}]}"""
            );
        var client = Client(handler);

        var result = await client.FindMediaByPathAsync("Images/Logos", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Logos", Assert.Single(result.Data!).Name);
    }
}
