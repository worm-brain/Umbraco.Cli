using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

public class UmbracoManagementClientTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw _ex;
    }

    private static UmbracoManagementClient ClientThatThrows(Exception ex) =>
        new(new HttpClient(new ThrowingHandler(ex)) { BaseAddress = new Uri("https://example.com/") });

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetContentByIdAsync(Guid.NewGuid(), cts.Token));
    }
}
