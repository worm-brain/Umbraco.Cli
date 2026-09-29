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
    public async Task CreateStaticFileAsync_PostsNameAndContent()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateStaticFileAsync(
                StaticFileKind.Stylesheet,
                new CreateStaticFileRequest
                {
                    Name = "site.css",
                    ParentPath = "theme",
                    Content = "body{}",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/stylesheet");
        Assert.Equal(("site.css", "body{}"), ((string?)body["name"], (string?)body["content"]));
    }

    [Fact]
    public async Task CreateStaticFileAsync_SlashedParent_SendsItTrimmed()
    {
        // #296: "/theme/" as typed (or "/theme" as list prints it) is the folder "theme".
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateStaticFileAsync(
                StaticFileKind.Stylesheet,
                new CreateStaticFileRequest { Name = "site.css", ParentPath = "/theme/" },
                CancellationToken.None
            );

        Assert.Equal(
            "theme",
            (string?)handler.BodyOf(HttpMethod.Post, "/stylesheet")["parent"]!["path"]
        );
    }

    [Fact]
    public async Task CreateStaticFileAsync_ReadsTheFileBackAndReturnsItAsGetShowsIt()
    {
        // #296: the result is the saved file, with Umbraco's leading-slash path.
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                """{"path":"/theme/site.css","name":"site.css","parent":{"path":"/theme"}}"""
            )
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateStaticFileAsync(
                StaticFileKind.Stylesheet,
                new CreateStaticFileRequest { Name = "site.css", ParentPath = "theme/" },
                CancellationToken.None
            );

        Assert.Equal(
            ("/theme/site.css", "%2Ftheme%2Fsite.css"),
            (
                result.Data!.Path,
                handler.AssertRequested(HttpMethod.Get, "site.css").Uri.AbsolutePath.Split('/')[^1]
            )
        );
    }

    [Fact]
    public async Task CreateStaticFileAsync_ReadBackFails_ReturnsTheNormalisedPath()
    {
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.NotFound, "")
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateStaticFileAsync(
                StaticFileKind.Script,
                new CreateStaticFileRequest { Name = "site.js", ParentPath = "/lib/" },
                CancellationToken.None
            );

        Assert.Equal(
            (true, "/lib/site.js", "/lib"),
            (result.IsSuccess, result.Data!.Path, result.Data.ParentPath)
        );
    }

    [Fact]
    public async Task CreateStaticFileAsync_CreateRejected_ReturnsTheFailure()
    {
        var (client, _) = ClientReturning("""{"title":"Name taken"}""", HttpStatusCode.BadRequest);

        var result = await client.CreateStaticFileAsync(
            StaticFileKind.Script,
            new CreateStaticFileRequest { Name = "site.js" },
            CancellationToken.None
        );

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task CreateStaticFileFolderAsync_PostsNameAndTrimmedParent()
    {
        // #238: the folder endpoint, with the parent in the form the live round showed working.
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateStaticFileFolderAsync(
                StaticFileKind.PartialView,
                "Components",
                "/blocklist/",
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/partial-view/folder");
        Assert.Equal(
            ("Components", "blocklist"),
            ((string?)body["name"], (string?)body["parent"]!["path"])
        );
    }

    [Fact]
    public async Task CreateStaticFileFolderAsync_ReadsTheFolderBack()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                """{"path":"/blocklist/Components","name":"Components","parent":{"path":"/blocklist"}}"""
            )
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateStaticFileFolderAsync(
                StaticFileKind.Stylesheet,
                "Components",
                "blocklist",
                CancellationToken.None
            );

        Assert.Equal(
            ("/blocklist/Components", "/blocklist"),
            (result.Data!.Path, result.Data.ParentPath)
        );
    }

    [Fact]
    public async Task CreateStaticFileFolderAsync_ReadBackFails_ReturnsTheWorkedOutPath()
    {
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.NotFound, "")
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateStaticFileFolderAsync(
                StaticFileKind.Script,
                "lib",
                null,
                CancellationToken.None
            );

        Assert.Equal((true, "/lib"), (result.IsSuccess, result.Data!.Path));
    }

    [Fact]
    public async Task DeleteStaticFileFolderAsync_DeletesTheFolderPath()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .DeleteStaticFileFolderAsync(StaticFileKind.Script, "lib", CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, "/script/folder/lib");
    }

    [Fact]
    public async Task DeleteStaticFileFolderAsync_NotEmpty_ReturnsTheFailure()
    {
        var (client, _) = ClientReturning(
            """{"title":"Not empty","status":400}""",
            HttpStatusCode.BadRequest
        );

        var result = await client.DeleteStaticFileFolderAsync(
            StaticFileKind.PartialView,
            "blocklist",
            CancellationToken.None
        );

        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("/", null)]
    [InlineData(" /blocklist/Components/ ", "blocklist/Components")]
    [InlineData("theme", "theme")]
    public void NormaliseFolder_TrimsSlashes(string? typed, string? expected)
    {
        Assert.Equal(expected, UmbracoManagementClient.NormaliseFolder(typed));
    }

    [Theory]
    [InlineData(null, "/site.css")]
    [InlineData("theme/dark", "/theme/dark/site.css")]
    public void CreatedPath_IsUmbracosLeadingSlashForm(string? parent, string expected)
    {
        var path = UmbracoManagementClient.CreatedPath(
            new CreateStaticFileRequest { Name = "site.css", ParentPath = parent }
        );

        Assert.Equal(expected, path);
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

    [Theory]
    [InlineData(StaticFileKind.Script, null, "/tree/script/root?skip=40&take=20")]
    [InlineData(StaticFileKind.Stylesheet, null, "/tree/stylesheet/root?skip=40&take=20")]
    [InlineData(StaticFileKind.PartialView, null, "/tree/partial-view/root?skip=40&take=20")]
    [InlineData(
        StaticFileKind.Script,
        "lib",
        "/tree/script/children?parentPath=lib&skip=40&take=20"
    )]
    [InlineData(
        StaticFileKind.Stylesheet,
        "lib",
        "/tree/stylesheet/children?parentPath=lib&skip=40&take=20"
    )]
    [InlineData(
        StaticFileKind.PartialView,
        "lib",
        "/tree/partial-view/children?parentPath=lib&skip=40&take=20"
    )]
    public async Task GetStaticFilesAsync_EachKindAndLevel_SendsThePageItAskedFor(
        StaticFileKind kind,
        string? parent,
        string expectedPathAndQuery
    )
    {
        // #426: the paging used to be set through `dynamic`; each typed query must still carry it.
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        await client.GetStaticFilesAsync(kind, parent, 40, 20, CancellationToken.None);

        Assert.EndsWith(expectedPathAndQuery, handler.LastUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetStaticFilesAsync_ApiError_ReturnsTheFailure()
    {
        var (client, _) = ClientReturning("""{"title":"Boom"}""", HttpStatusCode.BadRequest);

        var result = await client.GetStaticFilesAsync(
            StaticFileKind.Script,
            null,
            0,
            20,
            CancellationToken.None
        );

        Assert.Equal((false, 400), (result.IsSuccess, result.StatusCode));
    }

    [Theory]
    [InlineData(StaticFileKind.Script, "/script/lib%2Fsite.js/rename")]
    [InlineData(StaticFileKind.Stylesheet, "/stylesheet/lib%2Fsite.js/rename")]
    [InlineData(StaticFileKind.PartialView, "/partial-view/lib%2Fsite.js/rename")]
    public async Task RenameStaticFileAsync_PutsTheNewNameToTheKindsRenameEndpoint(
        StaticFileKind kind,
        string expectedPath
    )
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        await client.RenameStaticFileAsync(kind, "lib/site.js", "main.js", CancellationToken.None);

        Assert.Equal(
            (HttpMethod.Put, true, """{"name":"main.js"}"""),
            (
                handler.LastMethod,
                handler.LastUri!.AbsoluteUri.EndsWith(expectedPath),
                handler.LastBody
            )
        );
    }

    [Fact]
    public async Task RenameStaticFileAsync_ApiRefuses_ReturnsAFailure()
    {
        var (client, _) = ClientReturning(
            """{"title":"Invalid file extension"}""",
            HttpStatusCode.BadRequest
        );

        var result = await client.RenameStaticFileAsync(
            StaticFileKind.Stylesheet,
            "site.css",
            "site",
            CancellationToken.None
        );

        Assert.Equal((false, 400), (result.IsSuccess, result.StatusCode));
    }

    [Theory]
    [InlineData("site.css", "main.css", "main.css")]
    [InlineData("/site.css", "main.css", "/main.css")]
    [InlineData("/theme/site.css", "main.css", "/theme/main.css")]
    [InlineData("theme/nested/site.css", "main.css", "theme/nested/main.css")]
    public void RenamedPath_KeepsTheFolderAndSwapsTheName(string path, string name, string expected)
    {
        Assert.Equal(expected, UmbracoManagementClient.RenamedPath(path, name));
    }
}
