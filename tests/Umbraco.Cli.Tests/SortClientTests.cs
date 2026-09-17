using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the content/media sort client methods (#88): each hits the right sort endpoint with
/// a PUT, and the order of the supplied child ids is turned into ascending <c>sortOrder</c> values
/// (index 0, 1, 2, ...). Uses a handler that records the request URI, method, and body, over the
/// real client.
/// </summary>
public class SortClientTests
{
    private sealed record Recorded(HttpMethod Method, Uri Uri, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Recorded(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json"),
            };
        }

        public Recorded First(HttpMethod method) => Requests.First(r => r.Method == method);
    }

    private static (UmbracoManagementClient Client, RecordingHandler Handler) Build()
    {
        var handler = new RecordingHandler();
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    /// <summary>The order of the ids becomes ascending sort orders under a <c>document/sort</c> PUT.</summary>
    [Fact]
    public async Task SortContentAsync_PutsSortingInGivenOrderWithParent()
    {
        var (client, handler) = Build();
        var parent = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var result = await client.SortContentAsync(parent, [a, b, c], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var put = handler.First(HttpMethod.Put);
        Assert.EndsWith("/document/sort", put.Uri.AbsolutePath);
        var body = JsonNode.Parse(put.Body!)!;
        Assert.Equal(parent.ToString(), body["parent"]!["id"]!.GetValue<string>());
        var sorting = body["sorting"]!.AsArray();
        // Positions map 1:1 to sortOrder 0/1/2, preserving the caller's order.
        Assert.Equal(a.ToString(), sorting[0]!["id"]!.GetValue<string>());
        Assert.Equal(0, sorting[0]!["sortOrder"]!.GetValue<int>());
        Assert.Equal(b.ToString(), sorting[1]!["id"]!.GetValue<string>());
        Assert.Equal(1, sorting[1]!["sortOrder"]!.GetValue<int>());
        Assert.Equal(c.ToString(), sorting[2]!["id"]!.GetValue<string>());
        Assert.Equal(2, sorting[2]!["sortOrder"]!.GetValue<int>());
    }

    /// <summary>A null parent reorders the content root: the request carries no parent object.</summary>
    [Fact]
    public async Task SortContentAsync_NullParent_SendsNoParent()
    {
        var (client, handler) = Build();
        var a = Guid.NewGuid();

        var result = await client.SortContentAsync(null, [a], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var body = JsonNode.Parse(handler.First(HttpMethod.Put).Body!)!;
        Assert.Null(body["parent"]);
    }

    /// <summary>Media sort hits the media sort endpoint (distinct from the document one).</summary>
    [Fact]
    public async Task SortMediaAsync_PutsToMediaSortEndpoint()
    {
        var (client, handler) = Build();
        var parent = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = await client.SortMediaAsync(parent, [a, b], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var put = handler.First(HttpMethod.Put);
        Assert.EndsWith("/media/sort", put.Uri.AbsolutePath);
        var sorting = JsonNode.Parse(put.Body!)!["sorting"]!.AsArray();
        Assert.Equal(a.ToString(), sorting[0]!["id"]!.GetValue<string>());
        Assert.Equal(b.ToString(), sorting[1]!["id"]!.GetValue<string>());
    }
}
