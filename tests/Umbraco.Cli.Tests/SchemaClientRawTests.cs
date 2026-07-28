using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests the raw-JSON schema methods on <see cref="UmbracoManagementClient"/> (#68 / ADR
/// 0004): reads must hit the correct by-id endpoint and return the verbatim body without the
/// lossy projection, and writes must POST/PUT the body to the correct endpoint. Uses the same
/// stub-handler pattern as <see cref="UmbracoManagementClientTests"/>.
/// </summary>
public class SchemaClientRawTests
{
    /// <summary>Records requests and returns a canned body/status, mirroring the client-test stub.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
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
    public async Task GetDocumentTypeRawAsync_HitsByIdEndpoint_AndReturnsVerbatimBody()
    {
        // The verbatim body carries the rich collections the lossy DocumentTypeResponse drops
        // (properties here) — the whole point of the raw read.
        var id = Guid.NewGuid();
        var json = $$"""
            {"id":"{{id}}","alias":"blogPost","name":"Blog Post",
             "properties":[{"alias":"summary"}],"compositions":[]}
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetDocumentTypeRawAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"document-type/{id}", handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        // The property that the typed record would have dropped survives on the raw node.
        Assert.Equal("summary", (string?)result.Data!["properties"]![0]!["alias"]);
    }

    [Fact]
    public async Task GetDataTypeRawAsync_PreservesConfigValues()
    {
        // DataTypeResponse has no "values" field; the raw read must keep it.
        var id = Guid.NewGuid();
        var json = $$"""
            {"id":"{{id}}","name":"My Slider","editorAlias":"Umbraco.Slider",
             "values":[{"alias":"min","value":0},{"alias":"max","value":100}]}
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetDataTypeRawAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"data-type/{id}", handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal(2, result.Data!["values"]!.AsArray().Count);
    }

    [Fact]
    public async Task GetTemplateRawAsync_PreservesRazorContent()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {"id":"{{id}}","alias":"home","name":"Home","content":"@inherits UmbracoViewPage"}
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetTemplateRawAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"template/{id}", handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal("@inherits UmbracoViewPage", (string?)result.Data!["content"]);
    }

    [Fact]
    public async Task GetDocumentTypeRawAsync_MapsHttpErrorToFailure()
    {
        var (client, _) = ClientReturning("""{"title":"Not Found"}""", HttpStatusCode.NotFound);

        var result = await client.GetDocumentTypeRawAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task CreateDocumentTypeRawAsync_PostsBodyToCollectionEndpoint()
    {
        // Umbraco writes return 201 with an empty body; the client treats 2xx-empty as success.
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);
        var body = JsonNode.Parse("""{"alias":"blogPost","name":"Blog Post"}""")!;

        var result = await client.CreateDocumentTypeRawAsync(body, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("document-type", handler.LastRequestUri!.AbsolutePath);
        Assert.Contains("blogPost", handler.LastBody);
    }

    [Fact]
    public async Task UpdateTemplateRawAsync_PutsBodyToByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);
        var body = JsonNode.Parse("""{"alias":"home","content":"@* changed *@"}""")!;

        var result = await client.UpdateTemplateRawAsync(id, body, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains($"template/{id}", handler.LastRequestUri!.AbsoluteUri);
        Assert.Contains("changed", handler.LastBody);
    }
}
