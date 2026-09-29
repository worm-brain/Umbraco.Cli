using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the schema snapshot's newer kinds (#227) put on the wire: every page of a collection is
/// read (a short read would export as "these were deleted"), the dictionary overview yields each
/// item's parent, and a language is written by ISO code without an <c>isoCode</c> in the body.
/// </summary>
public class SchemaBreadthWireTests
{
    /// <summary>A <c>{total, items}</c> page of <paramref name="count"/> member groups.</summary>
    private static string Page(int count, int total) =>
        new JsonObject
        {
            ["total"] = total,
            ["items"] = new JsonArray([
                .. Enumerable
                    .Range(0, count)
                    .Select(_ => (JsonNode)new JsonObject { ["id"] = Guid.NewGuid().ToString() }),
            ]),
        }.ToJsonString();

    [Fact]
    public async Task GetMemberGroupsRawAsync_ReadsEveryPage()
    {
        // Arrange: a full first page and a short second one.
        var handler = new RoutingHandler()
            .When(r => r.RequestUri!.Query.Contains("skip=0"), HttpStatusCode.OK, Page(100, 101))
            .When(r => r.RequestUri!.Query.Contains("skip=100"), HttpStatusCode.OK, Page(1, 101));

        // Act
        var result = await Wire.Client(handler).GetMemberGroupsRawAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(101, result.Data!.Count);
    }

    [Fact]
    public async Task GetMemberGroupsRawAsync_FailedPage_IsAFailureNotAShortList()
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.InternalServerError,
            """{"title":"boom"}"""
        );

        var result = await Wire.Client(handler).GetMemberGroupsRawAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetDictionaryEntriesAsync_ReturnsEachItemsParent()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "/dictionary",
                $$$"""
                {"total":2,"items":[
                  {"id":"{{{root}}}","name":"Blog","parent":null},
                  {"id":"{{{child}}}","name":"Blog.Title","parent":{"id":"{{{root}}}"}}]}
                """
            )
        );

        var result = await Wire.Client(handler).GetDictionaryEntriesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            new DictionaryEntry[] { new(root, null), new(child, root) },
            result.Data!.ToArray()
        );
    }

    [Fact]
    public async Task UpdateLanguageRawAsync_PutsToTheIsoCodeWithoutIsoCodeInTheBody()
    {
        var handler = Wire.Blank();
        var body = JsonNode.Parse(
            """{"isoCode":"da-DK","name":"Danish","isDefault":false,"isMandatory":false,"fallbackIsoCode":"en-US"}"""
        )!;

        var result = await Wire.Client(handler)
            .UpdateLanguageRawAsync("da-DK", body, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(
                    """{"name":"Danish","isDefault":false,"isMandatory":false,"fallbackIsoCode":"en-US"}"""
                ),
                handler.BodyOf(HttpMethod.Put, "/umbraco/management/api/v1/language/da-DK")
            ),
            "The PUT must carry the body minus its isoCode."
        );
    }

    [Fact]
    public async Task CreateLanguageRawAsync_PostsTheBodyVerbatim()
    {
        var handler = Wire.Blank();
        var body = JsonNode.Parse("""{"isoCode":"da-DK","name":"Danish"}""")!;

        await Wire.Client(handler).CreateLanguageRawAsync(body, CancellationToken.None);

        Assert.True(
            JsonNode.DeepEquals(
                body,
                handler.BodyOf(HttpMethod.Post, "/umbraco/management/api/v1/language")
            )
        );
    }
}
