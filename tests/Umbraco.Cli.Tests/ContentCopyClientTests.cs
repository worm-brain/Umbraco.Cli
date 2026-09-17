using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of <see cref="UmbracoManagementClient.CopyContentAsync"/> (#91): the copy endpoint
/// returns 201 with the new id only in the Location header, which the generated method discards; the
/// client reads it via a native response handler and hydrates the new node. Uses a handler that
/// serves the 201+Location on the copy POST and a document body on the follow-up hydration GET.
/// </summary>
public class ContentCopyClientTests
{
    private sealed class CopyHandler(Guid? locationId) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add(request.RequestUri!);
            if (
                request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/copy")
            )
            {
                var created = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json"),
                };
                if (locationId is { } lid)
                    created.Headers.Location = new Uri(
                        $"https://example.com/umbraco/management/api/v1/document/{lid}"
                    );
                return Task.FromResult(created);
            }

            // Hydration GET document/{id}: return a document body so the mapped name is populated.
            var body = "{\"id\":\"" + locationId + "\",\"variants\":[{\"name\":\"About (copy)\"}]}";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static UmbracoManagementClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    [Fact]
    public async Task CopyContentAsync_ReadsLocationHeaderAndHydratesNewNode()
    {
        var newId = Guid.NewGuid();
        var handler = new CopyHandler(newId);
        var client = Client(handler);

        var result = await client.CopyContentAsync(Guid.NewGuid(), ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newId, result.Data!.Id); // id came from the Location header
        Assert.Equal("About (copy)", result.Data!.Name); // hydrated from the follow-up GET
        Assert.Contains(handler.Requests, u => u.AbsoluteUri.Contains($"document/{newId}"));
    }

    [Fact]
    public async Task CopyContentAsync_NoLocationHeader_SucceedsWithoutId()
    {
        // A 201 without a Location header (or an unparseable one) must not crash: the copy still
        // succeeded, we just cannot surface an id.
        var handler = new CopyHandler(locationId: null);
        var client = Client(handler);

        var result = await client.CopyContentAsync(Guid.NewGuid(), ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Empty, result.Data!.Id);
        // With no id to hydrate, only the copy POST is made - no follow-up GET.
        Assert.Single(handler.Requests);
    }
}
