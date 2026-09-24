using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the wire-assertion helpers themselves (#187 Phase 2). These guard the guard: the
/// whole point of <see cref="Wire"/> is that a predicate matching nothing fails loudly, so if
/// that stopped working, ~80 assertions built on it would quietly stop asserting anything.
/// </summary>
public class WireTests
{
    /// <summary>A handler that answers everything with 200 and an empty body.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Any() =>
        new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");

    [Fact]
    public async Task BodyForFirst_NoMatchingRequest_Throws()
    {
        var handler = Any();
        await Wire.Client(handler).DeleteContentAsync(Guid.NewGuid(), CancellationToken.None);

        // The hazard this replaces: returning "" here made every DoesNotContain assertion pass
        // against a request that was never made.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            handler.BodyForFirst(r => Wire.PathEnds(r, "/nothing/like/this"))
        );
        Assert.Contains("No recorded request matched", ex.Message);
    }

    [Fact]
    public void BodyForFirst_NoRequestsAtAll_SaysSo()
    {
        var handler = Any();

        var ex = Assert.Throws<InvalidOperationException>(() => handler.BodyForFirst(_ => true));
        Assert.Contains("No requests were made at all", ex.Message);
    }

    [Fact]
    public async Task BodyOf_WrongMethod_ThrowsAndNamesWhatWasSent()
    {
        var id = Guid.NewGuid();
        var handler = Any();
        await Wire.Client(handler).DeleteContentAsync(id, CancellationToken.None);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            handler.BodyOf(HttpMethod.Put, $"/document/{id}")
        );
        Assert.Contains("No PUT request", ex.Message);
        Assert.Contains("document", ex.Message);
    }

    [Fact]
    public async Task BodyOf_MatchingRequest_ReturnsTheParsedBody()
    {
        var id = Guid.NewGuid();
        var handler = Any();

        await Wire.Client(handler).PublishContentAsync(id, ["en-US"], ct: CancellationToken.None);

        var body = handler.BodyOf(HttpMethod.Put, $"/document/{id}/publish");
        Assert.NotNull(body["publishSchedules"]);
    }

    [Fact]
    public async Task AssertNoRequest_WhenOneWasMade_Throws()
    {
        var id = Guid.NewGuid();
        var handler = Any();
        await Wire.Client(handler).DeleteContentAsync(id, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() =>
            handler.AssertNoRequest(HttpMethod.Delete, $"/document/{id}")
        );
    }

    [Fact]
    public async Task AssertNoRequest_WhenNoneWasMade_Passes()
    {
        var handler = Any();
        await Wire.Client(handler).DeleteContentAsync(Guid.NewGuid(), CancellationToken.None);

        handler.AssertNoRequest(HttpMethod.Put, "/document/never");
    }

    [Fact]
    public async Task QueryOf_ReturnsTheParsedQueryString()
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.OK,
            """{"total":0,"items":[]}"""
        );

        await Wire.Client(handler).GetContentAsync(null, 5, 10, CancellationToken.None);

        var query = handler.QueryOf(HttpMethod.Get, "document");
        Assert.Equal("5", query["skip"]);
        Assert.Equal("10", query["take"]);
    }
}
