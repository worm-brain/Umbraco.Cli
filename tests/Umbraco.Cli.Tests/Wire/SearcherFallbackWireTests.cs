using System.Net;

namespace Umbraco.Cli.Tests;

/// <summary>
/// #244: on Umbraco 17 an index's <c>searcherName</c> is not a registered searcher (404), but the
/// index name is queryable, so the client retries a searcher 404 with the matching index.
/// </summary>
public class SearcherFallbackWireTests
{
    private static RoutingHandler Umbraco17() =>
        new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/searcher/ExternalSearcher/query"),
                HttpStatusCode.NotFound,
                """{"title":"Searcher not found","detail":"did not match any of our registered searchers"}"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/searcher/ExternalIndex/query"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"id":"1066","score":1.5,"fields":[]}]}"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/indexer"),
                HttpStatusCode.OK,
                """{"total":1,"items":[{"name":"ExternalIndex","searcherName":"ExternalSearcher","healthStatus":{"status":"Healthy"},"canRebuild":true,"documentCount":10,"fieldCount":5,"providerProperties":{}}]}"""
            )
            .When(_ => true, HttpStatusCode.NotFound, "{}");

    [Fact]
    public async Task QuerySearcherAsync_TheIndexsSearcherName_IsAnsweredByTheIndex()
    {
        var result = await Wire.Client(Umbraco17())
            .QuerySearcherAsync("ExternalSearcher", "Docker");

        Assert.Equal("1066", Assert.Single(result.Data!.Items).Id);
    }

    [Fact]
    public async Task QuerySearcherAsync_AnUnknownName_StillFailsWith404()
    {
        var result = await Wire.Client(Umbraco17()).QuerySearcherAsync("NoSuchSearcher", "Docker");

        Assert.Equal(404, result.StatusCode);
    }
}
