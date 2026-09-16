using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the diagnostics client methods (#115): each noun hits the right endpoint and verb,
/// the log query passes its level/order filters, the manifest scope selects the right sub-path, and
/// the action endpoints (health run, saved-search create/delete, models-builder build) use the right
/// HTTP method. Uses a recording stub over the real client.
/// </summary>
public class DiagnosticsClientTests
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
    public async Task GetServerStatusAsync_ReadsServerStatusEndpoint()
    {
        var (client, handler) = ClientReturning("""{"serverStatus":"Run"}""");

        var result = await client.GetServerStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/server/status", handler.LastUri!.AbsolutePath);
        Assert.Equal("Run", result.Data!.ServerStatus);
    }

    [Fact]
    public async Task GetHealthCheckGroupsAsync_ReadsGroupCollection()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"name":"Data Integrity"}]}"""
        );

        var result = await client.GetHealthCheckGroupsAsync(0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/health-check-group", handler.LastUri!.AbsolutePath);
        Assert.Equal("Data Integrity", Assert.Single(result.Data!.Items).Name);
    }

    [Fact]
    public async Task RunHealthCheckGroupAsync_PostsToCheckEndpoint()
    {
        var (client, handler) = ClientReturning("""{"checks":[]}""");

        var result = await client.RunHealthCheckGroupAsync("Services", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/health-check-group/Services/check", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetLogsAsync_PassesLevelAndOrderFilters()
    {
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetLogsAsync(
            0,
            50,
            levels: ["Error"],
            filterExpression: null,
            startDate: null,
            endDate: null,
            descending: true,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/log-viewer/log", handler.LastUri!.AbsolutePath);
        Assert.Contains("logLevel=Error", handler.LastUri.AbsoluteUri);
        Assert.Contains("orderDirection=Descending", handler.LastUri.AbsoluteUri);
    }

    [Fact]
    public async Task GetLogLevelCountsAsync_ReadsLevelCountEndpoint()
    {
        var (client, handler) = ClientReturning(
            """{"debug":1,"information":2,"warning":3,"error":4,"fatal":5}"""
        );

        var result = await client.GetLogLevelCountsAsync(null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/log-viewer/level-count", handler.LastUri!.AbsolutePath);
        Assert.Equal(4, result.Data!.Error);
    }

    [Fact]
    public async Task CreateSavedLogSearchAsync_PostsNameAndQuery()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateSavedLogSearchAsync(
            "Errors",
            "@Level='Error'",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/log-viewer/saved-search", handler.LastUri!.AbsolutePath);
        Assert.Contains("Errors", handler.LastBody);
        Assert.Equal("Errors", result.Data!.Name);
    }

    [Fact]
    public async Task DeleteSavedLogSearchAsync_DeletesByName()
    {
        var (client, handler) = ClientReturning("");

        var result = await client.DeleteSavedLogSearchAsync("Errors", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.EndsWith("/log-viewer/saved-search/Errors", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetModelsBuilderDashboardAsync_ReadsDashboardEndpoint()
    {
        var (client, handler) = ClientReturning("""{"mode":"InMemoryAuto","canGenerate":true}""");

        var result = await client.GetModelsBuilderDashboardAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/models-builder/dashboard", handler.LastUri!.AbsolutePath);
        Assert.True(result.Data!.CanGenerate);
    }

    [Fact]
    public async Task BuildModelsAsync_PostsToBuildEndpoint()
    {
        var (client, handler) = ClientReturning("");

        var result = await client.BuildModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/models-builder/build", handler.LastUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetManifestsAsync_All_ReadsManifestArray()
    {
        var (client, handler) = ClientReturning("""[{"id":"a","name":"Pkg","version":"1.0"}]""");

        var result = await client.GetManifestsAsync(ManifestScope.All, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/manifest/manifest", handler.LastUri!.AbsolutePath);
        Assert.Equal("Pkg", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task GetManifestsAsync_Public_ReadsPublicSubPath()
    {
        var (client, handler) = ClientReturning("[]");

        var result = await client.GetManifestsAsync(ManifestScope.Public, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/manifest/manifest/public", handler.LastUri!.AbsolutePath);
    }
}
