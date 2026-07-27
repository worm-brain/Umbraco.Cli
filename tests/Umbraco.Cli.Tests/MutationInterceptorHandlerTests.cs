using System.Net;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the <see cref="MutationInterceptorHandler"/> that backs <c>--dry-run</c> (#62):
/// under the Preview policy a state-changing request is captured and aborted before it is
/// sent; everything else passes through untouched.
/// </summary>
public class MutationInterceptorHandlerTests
{
    /// <summary>Inner handler that records whether it was reached and returns 200.</summary>
    private sealed class OkHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, OkHandler Inner) Build(MutationInterceptPolicy policy)
    {
        var inner = new OkHandler();
        var handler = new MutationInterceptorHandler(new MutationInterceptState { Policy = policy })
        {
            InnerHandler = inner,
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        return (client, inner);
    }

    [Fact]
    public async Task Preview_MutatingRequest_ThrowsWithCapturedDetailsAndDoesNotSend()
    {
        var (client, inner) = Build(MutationInterceptPolicy.Preview);

        var ex = await Assert.ThrowsAsync<DryRunException>(() =>
            client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("""{"a":1}"""))
        );

        Assert.Equal("POST", ex.Method);
        Assert.EndsWith("/umbraco/management/api/v1/webhook", ex.Url);
        Assert.Contains("\"a\":1", ex.Body);
        Assert.False(inner.WasCalled); // the request was never forwarded to the wire
    }

    [Fact]
    public async Task Preview_ReadRequest_PassesThrough()
    {
        // A write flow can make reads first (alias->id resolution); those must still run so
        // the preview reflects the real final mutation.
        var (client, inner) = Build(MutationInterceptPolicy.Preview);

        var response = await client.GetAsync("umbraco/management/api/v1/language");

        Assert.True(inner.WasCalled);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Block_MutatingRequest_ThrowsReadOnlyAndDoesNotSend()
    {
        // #69: under Block (--readonly) a write is refused before it reaches the wire.
        var (client, inner) = Build(MutationInterceptPolicy.Block);

        var ex = await Assert.ThrowsAsync<ReadOnlyModeException>(() =>
            client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("{}"))
        );

        Assert.Equal("POST", ex.Method);
        Assert.EndsWith("/umbraco/management/api/v1/webhook", ex.Url);
        Assert.False(inner.WasCalled);
    }

    [Fact]
    public async Task Block_ReadRequest_PassesThrough()
    {
        // Read-only still allows reads.
        var (client, inner) = Build(MutationInterceptPolicy.Block);

        await client.GetAsync("umbraco/management/api/v1/language");

        Assert.True(inner.WasCalled);
    }

    [Fact]
    public async Task Execute_MutatingRequest_PassesThrough()
    {
        // Default policy: writes are sent as normal (no dry-run).
        var (client, inner) = Build(MutationInterceptPolicy.Execute);

        await client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("{}"));

        Assert.True(inner.WasCalled);
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    public void IsMutating_ClassifiesMethods(string method, bool expected) =>
        Assert.Equal(expected, MutationInterceptorHandler.IsMutating(new HttpMethod(method)));
}
