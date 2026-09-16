using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the coverage-finale client methods (#121): Examine (indexer/searcher), imaging, the
/// data-type advanced verbs, and property-type is-used each hit the right endpoint and HTTP method
/// and pass their key query params. Uses a recording stub over the real client.
/// </summary>
public class CoverageFinaleClientTests
{
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (UmbracoManagementClient Client, StubHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        var handler = new StubHandler(json, status);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    // ── Examine ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIndexersAsync_ReadsIndexerCollectionAndMaps()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"name":"ExternalIndex","documentCount":42,"canRebuild":true}]}"""
        );

        var result = await client.GetIndexersAsync(0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/indexer", handler.LastUri!.AbsolutePath);
        var i = Assert.Single(result.Data!.Items);
        Assert.Equal("ExternalIndex", i.Name);
        Assert.Equal(42, i.DocumentCount);
    }

    [Fact]
    public async Task RebuildIndexAsync_PostsToRebuildEndpoint()
    {
        var (client, handler) = ClientReturning("");

        var result = await client.RebuildIndexAsync("ExternalIndex", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/indexer/ExternalIndex/rebuild", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task QuerySearcherAsync_PassesTermToQueryEndpoint()
    {
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.QuerySearcherAsync(
            "ExternalSearcher",
            "news",
            0,
            20,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/searcher/ExternalSearcher/query", handler.LastUri!.AbsolutePath);
        Assert.Contains("term=news", handler.LastUri.AbsoluteUri);
    }

    // ── Imaging ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetResizeUrlsAsync_PassesIdsAndDimensions()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""[{"id":"{{id}}","urlInfos":[{"culture":null,"url":"/media/x.jpg?width=300"}]}]"""
        );

        var result = await client.GetResizeUrlsAsync(
            [id],
            width: 300,
            height: 200,
            mode: ImageResizeMode.Crop,
            format: "webp",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/imaging/resize/urls", handler.LastUri!.AbsolutePath);
        Assert.Contains("width=300", handler.LastUri.AbsoluteUri);
        Assert.Contains("mode=Crop", handler.LastUri.AbsoluteUri);
        var m = Assert.Single(result.Data!);
        Assert.Equal(id, m.Id);
    }

    // ── Data-type advanced ───────────────────────────────────────────────────────

    [Fact]
    public async Task IsDataTypeUsedAsync_ReadsIsUsedEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("true");

        var result = await client.IsDataTypeUsedAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/data-type/{id}/is-used", handler.LastUri!.AbsolutePath);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task CopyDataTypeAsync_PostsTargetToCopyEndpoint()
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.CopyDataTypeAsync(id, target, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith($"/data-type/{id}/copy", handler.LastUri!.AbsolutePath);
        Assert.Contains(target.ToString(), handler.LastBody);
    }

    [Fact]
    public async Task MoveDataTypeAsync_PutsTargetToMoveEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.MoveDataTypeAsync(id, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.EndsWith($"/data-type/{id}/move", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetDataTypeReferencedByRawAsync_ReadsReferencedByEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetDataTypeReferencedByRawAsync(
            id,
            0,
            100,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains($"data-type/{id}/referenced-by", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateDataTypeFolderAsync_PostsToFolderEndpointAndEchoesId()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateDataTypeFolderAsync(
            new CreateDataTypeFolderRequest { Id = id, Name = "Pickers" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/data-type/folder", handler.LastUri!.AbsolutePath);
        Assert.Equal(id, result.Data!.Id);
    }

    // ── Property-type ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsPropertyTypeUsedAsync_PassesContentTypeAndAlias()
    {
        var ctId = Guid.NewGuid();
        var (client, handler) = ClientReturning("true");

        var result = await client.IsPropertyTypeUsedAsync(ctId, "bodyText", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/property-type/is-used", handler.LastUri!.AbsolutePath);
        Assert.Contains($"contentTypeId={ctId}", handler.LastUri.AbsoluteUri);
        Assert.Contains("propertyAlias=bodyText", handler.LastUri.AbsoluteUri);
        Assert.True(result.Data);
    }
}
