using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

public class UmbracoManagementClientTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw _ex;
    }

    /// <summary>
    /// Test double that returns a canned JSON body + status for every request and records
    /// the last requested URI, so tests can assert both the endpoint the client called
    /// (the subject of issue #39) and how the response is mapped.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        /// <summary>The absolute URI of the most recent request the client made.</summary>
        public Uri? LastRequestUri { get; private set; }

        /// <param name="json">The response body to return.</param>
        /// <param name="status">The HTTP status to return (defaults to 200 OK).</param>
        public StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static UmbracoManagementClient ClientThatThrows(Exception ex) =>
        new(
            new HttpClient(new ThrowingHandler(ex))
            {
                BaseAddress = new Uri("https://example.com/"),
            }
        );

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
    public async Task TransportFailure_BecomesFailureNotException()
    {
        var client = ClientThatThrows(new HttpRequestException("no such host"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("reach", result.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_BecomesFailure()
    {
        // HttpClient surfaces a timeout as TaskCanceledException with no caller cancellation.
        var client = ClientThatThrows(new TaskCanceledException("timeout"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task GenuineCancellation_Propagates()
    {
        var client = ClientThatThrows(new HttpRequestException("unused"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetContentByIdAsync(Guid.NewGuid(), cts.Token)
        );
    }

    [Fact]
    public async Task GetContentAsync_NoParent_CallsTreeRootEndpoint()
    {
        // Regression for #39: root listing must hit the document tree, not the flat
        // (non-existent) /document collection.
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tree/document/root", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetContentAsync_FlattensVariantNameAndPublishedState()
    {
        // Regression for #39/#42: display name and published flag live under variants[],
        // not at the top level, and must be surfaced on the mapped item.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "total": 1,
              "items": [
                {
                  "id": "{{id}}",
                  "createDate": "2024-01-02T03:04:05+00:00",
                  "documentType": { "id": "11111111-1111-1111-1111-111111111111" },
                  "variants": [ { "culture": null, "name": "Home", "state": "Published" } ]
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("Home", item.Name);
        Assert.True(item.IsPublished);
    }

    [Fact]
    public async Task GetLanguagesAsync_ParsesPagedShape()
    {
        // Regression for #41: GET /language returns a paged {total,items} object, not a
        // bare array, and must deserialize instead of throwing.
        var json = """
            {
              "total": 1,
              "items": [
                { "isoCode": "en-US", "name": "English", "isDefault": true, "isMandatory": false }
              ]
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetLanguagesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/umbraco/management/api/v1/language", handler.LastRequestUri!.AbsolutePath);
        var lang = Assert.Single(result.Data!);
        Assert.Equal("en-US", lang.IsoCode);
        Assert.True(lang.IsDefault);
    }

    [Fact]
    public async Task GetContentAsync_ApiError_BecomesFailure()
    {
        // A non-2xx from a generated call is surfaced as a failed response (status + no
        // throw), preserving the "errors are data" contract through the Kiota guard.
        var (client, _) = ClientReturning(
            """{"title":"Not Found"}""",
            HttpStatusCode.NotFound
        );

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
