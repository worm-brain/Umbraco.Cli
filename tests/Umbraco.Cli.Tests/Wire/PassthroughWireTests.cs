using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What <see cref="UmbracoManagementClient.SendRawAsync"/> puts on the wire and hands back, the
/// client half of <c>umbraco api</c> (ADR 0010): the caller's method, path and body go out as
/// given, the response body comes back verbatim, and nothing outside the site's /umbraco/ is sent.
/// </summary>
public class PassthroughWireTests
{
    private const string Status = "/umbraco/management/api/v1/server/status";

    [Fact]
    public async Task SendRawAsync_Get_ReturnsTheResponseBodyVerbatim()
    {
        var client = Wire.Client(Wire.Returning("""{"serverStatus":"Run","extra":[1,2]}"""));

        var result = await client.SendRawAsync(HttpMethod.Get, Status);

        Assert.Equal("""{"serverStatus":"Run","extra":[1,2]}""", result.Data?.ToJsonString());
    }

    [Fact]
    public async Task SendRawAsync_PathWithQuery_IsRequestedFromTheHostRootAsGiven()
    {
        var handler = Wire.Returning("""{"total":0,"items":[]}""");

        await Wire.Client(handler)
            .SendRawAsync(HttpMethod.Get, "/umbraco/management/api/v1/tree/document/root?take=5");

        Assert.Equal(
            "https://example.com/umbraco/management/api/v1/tree/document/root?take=5",
            Assert.Single(handler.Requests).AbsoluteUri
        );
    }

    [Fact]
    public async Task SendRawAsync_HostWithAPathPrefix_KeepsThePrefix()
    {
        var handler = Wire.Returning("{}");
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/site/") }
        );

        await client.SendRawAsync(HttpMethod.Get, Status);

        Assert.Equal(
            "https://example.com/site/umbraco/management/api/v1/server/status",
            Assert.Single(handler.Requests).AbsoluteUri
        );
    }

    [Fact]
    public async Task SendRawAsync_PostWithABody_SendsTheBodyAsIs()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .SendRawAsync(
                HttpMethod.Post,
                "/umbraco/management/api/v1/language",
                JsonNode.Parse("""{"isoCode":"da-DK","name":"Danish","isDefault":false}""")
            );

        Assert.Equal(
            """{"isoCode":"da-DK","name":"Danish","isDefault":false}""",
            handler.RawBodyOf(HttpMethod.Post, "/language")
        );
    }

    [Fact]
    public async Task SendRawAsync_ResponseWithNoBody_IsAnEmptyObject()
    {
        var result = await Wire.Client(Wire.Blank())
            .SendRawAsync(HttpMethod.Delete, "/umbraco/management/api/v1/language/da-DK");

        Assert.Equal("{}", result.Data?.ToJsonString());
    }

    [Fact]
    public async Task SendRawAsync_BodyThatIsNotJson_IsKeptAsAString()
    {
        var result = await Wire.Client(Wire.Returning("plain words"))
            .SendRawAsync(HttpMethod.Get, "/umbraco/my-package/api/v1/ping");

        Assert.Equal("\"plain words\"", result.Data?.ToJsonString());
    }

    [Fact]
    public async Task SendRawAsync_ErrorResponse_PassesUmbracosBodyThroughAsDetails()
    {
        var client = Wire.Client(
            new RoutingHandler().When(
                _ => true,
                HttpStatusCode.NotFound,
                """{"title":"The language could not be found","status":404}"""
            )
        );

        var result = await client.SendRawAsync(
            HttpMethod.Get,
            "/umbraco/management/api/v1/language/xx-XX"
        );

        Assert.Equal(
            (false, 404, "The language could not be found"),
            (result.IsSuccess, result.StatusCode, (string?)result.Details?["title"])
        );
    }

    [Theory]
    [InlineData("/somewhere/else")]
    [InlineData("https://evil.example/umbraco/management/api/v1/server/status")]
    [InlineData("/umbraco/../somewhere/else")]
    public async Task SendRawAsync_PathOutsideUmbraco_IsRefusedWithNothingSent(string path)
    {
        var handler = Wire.Blank();

        var result = await Wire.Client(handler).SendRawAsync(HttpMethod.Get, path);

        Assert.Equal(
            (FailureCategory.InvalidArgument, 0),
            (result.Category, handler.Recordings.Count)
        );
    }

    [Fact]
    public async Task SendRawAsync_MethodItDoesNotSend_IsRefused()
    {
        var result = await Wire.Client(Wire.Blank()).SendRawAsync(HttpMethod.Head, Status);

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }
}
