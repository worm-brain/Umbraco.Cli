using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The server-assigned id of a copy is read from the 201 <c>Location</c> header (#91, #247), and a
/// rejected copy reports the server's error rather than "copied, but no Location" - reading the raw
/// response bypasses Kiota's error mapping, so the client has to map it itself.
/// </summary>
public class CopyLocationClientTests
{
    /// <summary>Answers the copy POST with <paramref name="copy"/>, and any GET with a small body.</summary>
    private sealed class Handler(Func<HttpResponseMessage> copy) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            // Every client test is a contract test (#76).
            ManagementSpec.AssertDeclared(request);
            return Task.FromResult(
                request.Method == HttpMethod.Post
                    ? copy()
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"name":"Copy","editorAlias":"Umbraco.TextBox","values":[]}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
            );
        }
    }

    private static UmbracoManagementClient Client(Func<HttpResponseMessage> copy) =>
        new(new HttpClient(new Handler(copy)) { BaseAddress = new Uri("https://example.com/") });

    private static HttpResponseMessage CreatedAt(Guid id)
    {
        var created = new HttpResponseMessage(HttpStatusCode.Created);
        created.Headers.Location = new Uri(
            $"https://example.com/umbraco/management/api/v1/data-type/{id}"
        );
        return created;
    }

    private static HttpResponseMessage Rejected() =>
        new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"Bad request","detail":"The target folder does not exist."}""",
                Encoding.UTF8,
                "application/problem+json"
            ),
        };

    [Fact]
    public async Task CopyDataTypeAsync_ReturnsTheIdFromTheLocationHeader()
    {
        var newId = Guid.NewGuid();

        var result = await Client(() => CreatedAt(newId)).CopyDataTypeAsync(Guid.NewGuid(), null);

        Assert.Equal(newId, result.Data!.Id);
    }

    [Fact]
    public async Task CopyDataTypeAsync_Rejected_ReportsTheServersError()
    {
        var result = await Client(Rejected).CopyDataTypeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(
            (400, "The target folder does not exist."),
            (result.StatusCode, result.ErrorMessage)
        );
    }

    [Fact]
    public async Task CopyContentAsync_Rejected_ReportsTheServersError()
    {
        var result = await Client(Rejected).CopyContentAsync(Guid.NewGuid());

        Assert.Equal(
            (400, "The target folder does not exist."),
            (result.StatusCode, result.ErrorMessage)
        );
    }
}
