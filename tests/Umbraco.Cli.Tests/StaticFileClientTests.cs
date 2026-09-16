using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the static-file client methods (#105): each <see cref="StaticFileKind"/> routes to
/// its own URL slug, the file path is percent-encoded into the request (slashes included), and the
/// verbatim content round-trips. Uses a recording stub handler over the real client.
/// </summary>
public class StaticFileClientTests
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

    [Fact]
    public async Task GetStaticFileAsync_Script_HitsScriptPath_AndReturnsContent()
    {
        var (client, handler) = ClientReturning(
            """{"path":"site.js","name":"site.js","content":"console.log(1)"}"""
        );

        var result = await client.GetStaticFileAsync(
            StaticFileKind.Script,
            "site.js",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Contains("/script/site.js", handler.LastUri!.AbsoluteUri);
        Assert.Equal("console.log(1)", result.Data!.Content);
    }

    [Fact]
    public async Task GetStaticFileAsync_EncodesSlashesInPath()
    {
        // Kiota percent-encodes the raw path (the {path} template uses simple expansion), so a
        // nested path's separators become %2F rather than extra URL segments.
        var (client, handler) = ClientReturning("""{"path":"lib/site.js","name":"site.js"}""");

        await client.GetStaticFileAsync(
            StaticFileKind.Script,
            "lib/site.js",
            CancellationToken.None
        );

        Assert.Contains(
            "lib%2Fsite.js",
            handler.LastUri!.AbsoluteUri,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(StaticFileKind.Script, "/script/")]
    [InlineData(StaticFileKind.Stylesheet, "/stylesheet/")]
    [InlineData(StaticFileKind.PartialView, "/partial-view/")]
    public async Task GetStaticFileAsync_RoutesToKindSlug(
        StaticFileKind kind,
        string expectedSegment
    )
    {
        var (client, handler) = ClientReturning("""{"path":"x","name":"x"}""");

        await client.GetStaticFileAsync(kind, "x", CancellationToken.None);

        Assert.Contains(expectedSegment, handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateStaticFileAsync_PostsNameAndContent_AndEchoesDerivedPath()
    {
        // Create returns an empty body; the client echoes the request with the derived path.
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateStaticFileAsync(
            StaticFileKind.Stylesheet,
            new CreateStaticFileRequest
            {
                Name = "site.css",
                ParentPath = "theme",
                Content = "body{}",
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/stylesheet", handler.LastUri!.AbsolutePath);
        Assert.Contains("site.css", handler.LastBody);
        Assert.Contains("body{}", handler.LastBody);
        // Path derived as parent/name for the echoed response.
        Assert.Equal("theme/site.css", result.Data!.Path);
    }

    [Fact]
    public async Task UpdateStaticFileAsync_PutsContentToPath()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.UpdateStaticFileAsync(
            StaticFileKind.Script,
            "site.js",
            new UpdateStaticFileRequest { Content = "// changed" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains("/script/site.js", handler.LastUri!.AbsoluteUri);
        Assert.Contains("changed", handler.LastBody);
    }

    [Fact]
    public async Task DeleteStaticFileAsync_DeletesPath()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteStaticFileAsync(
            StaticFileKind.PartialView,
            "grid/row.cshtml",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Contains("/partial-view/", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetStaticFilesAsync_MapsTreeItems()
    {
        var json = """
            {"total":2,"items":[
              {"path":"lib","name":"lib","isFolder":true,"hasChildren":true},
              {"path":"site.js","name":"site.js","isFolder":false,"hasChildren":false}
            ]}
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetStaticFilesAsync(
            StaticFileKind.Script,
            null,
            0,
            20,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("/tree/script/root", handler.LastUri!.AbsoluteUri);
        var items = result.Data!.Items.ToList();
        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsFolder);
        Assert.Equal("site.js", items[1].Name);
    }

    [Fact]
    public async Task GetStaticFilesAsync_WithParent_HitsChildrenEndpoint()
    {
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        await client.GetStaticFilesAsync(
            StaticFileKind.Stylesheet,
            "theme",
            0,
            20,
            CancellationToken.None
        );

        Assert.Contains("/tree/stylesheet/children", handler.LastUri!.AbsoluteUri);
        Assert.Contains("parentPath=theme", handler.LastUri!.AbsoluteUri);
    }
}
