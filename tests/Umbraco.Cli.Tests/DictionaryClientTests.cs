using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the dictionary tree/create-with-parent/move client methods (#110): create posts an
/// optional parent, the tree reads the root or children-by-parent endpoint, and move PUTs the target
/// to the move endpoint. Uses a handler that records the request sequence, over the real client.
/// </summary>
public class DictionaryClientTests
{
    private sealed record Recorded(HttpMethod Method, Uri Uri, string? Body);

    private sealed class RecordingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Recorded(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        public Recorded First(HttpMethod method) => Requests.First(r => r.Method == method);
    }

    private static (UmbracoManagementClient Client, RecordingHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        var handler = new RecordingHandler(json, status);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_WithParent_PostsParentReference()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);
        var parent = Guid.NewGuid();

        var result = await client.CreateDictionaryItemAsync(
            new CreateDictionaryItemRequest
            {
                Name = "Nav.Home",
                Parent = new ContentParentReference { Id = parent },
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var post = handler.First(HttpMethod.Post);
        Assert.EndsWith("/dictionary", post.Uri.AbsolutePath);
        Assert.Equal(
            parent.ToString(),
            JsonNode.Parse(post.Body!)!["parent"]!["id"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_NoParent_OmitsParent()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateDictionaryItemAsync(
            new CreateDictionaryItemRequest { Name = "Nav.Home" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(JsonNode.Parse(handler.First(HttpMethod.Post).Body!)!["parent"]);
    }

    [Fact]
    public async Task GetDictionaryTreeAsync_Root_ReadsTreeRoot()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","name":"General","hasChildren":true}]}"""
        );

        var result = await client.GetDictionaryTreeAsync(null, 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/tree/dictionary/root", handler.First(HttpMethod.Get).Uri.AbsolutePath);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("General", item.Name);
        Assert.True(item.HasChildren);
    }

    [Fact]
    public async Task GetDictionaryTreeAsync_WithParent_ReadsChildrenWithParentId()
    {
        // Fixed parent id (kept out of the raw JSON literal to avoid brace-interpolation escaping).
        var parent = Guid.Parse("1a2b3c4d-1234-5678-abcd-ef0123456789");
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"9c4d5e6f-1234-5678-abcd-ef0123456789","name":"Home","hasChildren":false,"parent":{"id":"1a2b3c4d-1234-5678-abcd-ef0123456789"}}]}"""
        );

        var result = await client.GetDictionaryTreeAsync(parent, 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var get = handler.First(HttpMethod.Get);
        Assert.EndsWith("/tree/dictionary/children", get.Uri.AbsolutePath);
        Assert.Contains($"parentId={parent}", get.Uri.AbsoluteUri);
        Assert.Equal(parent, Assert.Single(result.Data!.Items).Parent!.Id);
    }

    [Fact]
    public async Task MoveDictionaryItemAsync_PutsTargetToMoveEndpoint()
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.MoveDictionaryItemAsync(id, target, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var put = handler.First(HttpMethod.Put);
        Assert.EndsWith($"/dictionary/{id}/move", put.Uri.AbsolutePath);
        Assert.Equal(
            target.ToString(),
            JsonNode.Parse(put.Body!)!["target"]!["id"]!.GetValue<string>()
        );
    }
}
