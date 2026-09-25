using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the redirect and relation client methods (#118): each verb hits the right endpoint
/// and HTTP method, the redirect list passes its filter, the tracking toggle posts the status query,
/// and relations are listed by relation-type id. Uses a recording stub over the real client.
/// </summary>
public class RedirectRelationClientTests
{
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
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
    public async Task GetRedirectsAsync_PassesFilterAndMaps()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","originalUrl":"/old","destinationUrl":"/new"}]}"""
        );

        var result = await client.GetRedirectsAsync("old", 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/redirect-management", handler.LastUri!.AbsolutePath);
        Assert.Contains("filter=old", handler.LastUri.AbsoluteUri);
        var r = Assert.Single(result.Data!.Items);
        Assert.Equal("/old", r.OriginalUrl);
    }

    [Fact]
    public async Task GetRedirectsForContentAsync_ReadsByContentKey()
    {
        var key = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetRedirectsForContentAsync(key, 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/redirect-management/{key}", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetRedirectStatusAsync_MapsEnabled()
    {
        var (client, handler) = ClientReturning("""{"status":"Enabled","userIsAdmin":true}""");

        var result = await client.GetRedirectStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/redirect-management/status", handler.LastUri!.AbsolutePath);
        Assert.True(result.Data!.Enabled);
        Assert.True(result.Data.UserIsAdmin);
    }

    [Fact]
    public async Task DeleteRedirectAsync_DeletesByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.DeleteRedirectAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.EndsWith($"/redirect-management/{id}", handler.LastUri!.AbsolutePath);
    }

    /// <summary>A handler whose status re-read reports <paramref name="statusAfter"/>.</summary>
    private static RoutingHandler TrackingHandler(string statusAfter) =>
        new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{"status":"{{statusAfter}}","userIsAdmin":true}"""
            )
            .When(_ => true, HttpStatusCode.OK, "");

    [Fact]
    public async Task SetRedirectTrackingAsync_PostsStatusQuery()
    {
        var handler = TrackingHandler("Disabled");

        await Wire.Client(handler).SetRedirectTrackingAsync(false, CancellationToken.None);

        var post = handler.AssertRequested(HttpMethod.Post, "/redirect-management/status");
        Assert.Contains("status=Disabled", post.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task SetRedirectTrackingAsync_StatusChanged_Succeeds()
    {
        var handler = TrackingHandler("Disabled");

        var result = await Wire.Client(handler)
            .SetRedirectTrackingAsync(false, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SetRedirectTrackingAsync_UmbracoKeptTheOldStatus_FailsAndNamesTheSetting()
    {
        // #249: Umbraco 17 answers the POST with 200 and leaves tracking enabled; the CLI used
        // to report success anyway.
        var handler = TrackingHandler("Enabled");

        var result = await Wire.Client(handler)
            .SetRedirectTrackingAsync(false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
        Assert.Contains("DisableRedirectUrlTracking", result.ErrorMessage);
    }

    [Fact]
    public async Task GetRelationTypesAsync_ReadsCollectionAndMaps()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","alias":"relateDocumentOnCopy","name":"Relate on Copy","isBidirectional":false}]}"""
        );

        var result = await client.GetRelationTypesAsync(0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/relation-type", handler.LastUri!.AbsolutePath);
        Assert.Equal("relateDocumentOnCopy", Assert.Single(result.Data!.Items).Alias);
    }

    [Fact]
    public async Task GetRelationTypeByIdAsync_ReadsByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"id":"{{id}}","alias":"a","name":"A","isBidirectional":true,"isDependency":false}"""
        );

        var result = await client.GetRelationTypeByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/relation-type/{id}", handler.LastUri!.AbsolutePath);
        Assert.True(result.Data!.IsBidirectional);
    }

    [Fact]
    public async Task GetRelationsByTypeAsync_ReadsRelationTypeEndpointAndMaps()
    {
        var typeId = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","parent":{"id":"1a2b3c4d-1234-5678-abcd-ef0123456789","name":"Home"},"child":{"id":"2b3c4d5e-1234-5678-abcd-ef0123456789","name":"About"}}]}"""
        );

        var result = await client.GetRelationsByTypeAsync(typeId, 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/relation/type/{typeId}", handler.LastUri!.AbsolutePath);
        var r = Assert.Single(result.Data!.Items);
        Assert.Equal("Home", r.ParentName);
        Assert.Equal("About", r.ChildName);
    }
}
