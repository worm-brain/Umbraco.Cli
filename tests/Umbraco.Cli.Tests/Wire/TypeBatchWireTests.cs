using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Reading many schema items at once (#418): the four type kinds through their batch endpoint,
/// in request order; anything else, or an Umbraco without batch endpoints (before 17.3), one id
/// at a time; and an item the server does not return is a failure, never a silent gap. Also the
/// group lists, whose items are the whole body (#413).
/// </summary>
public class TypeBatchWireTests
{
    private static readonly Guid First = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("55555555-0000-0000-0000-000000000002");

    /// <summary>A minimal body for <paramref name="id"/>.</summary>
    private static string Body(Guid id) => $$"""{"id":"{{id}}","alias":"a{{id:N}}"}""";

    [Fact]
    public async Task GetSchemaRawManyAsync_TypeKind_ReadsOneBatchInTheOrderAsked()
    {
        // Arrange: the server lists the items in another order than asked.
        var handler = Wire.Routed(
            ("document-type/batch", $$"""{"total":2,"items":[{{Body(Second)}},{{Body(First)}}]}""")
        );

        // Act
        var result = await Wire.Client(handler)
            .GetSchemaRawManyAsync(EntityKind.DocumentType, [First, Second]);

        // Assert
        Assert.Equal(
            (true, 1, $"{First},{Second}"),
            (
                result.IsSuccess,
                handler.Recordings.Count,
                string.Join(",", result.Data!.Select(b => b["id"]!.GetValue<string>()))
            )
        );
    }

    [Fact]
    public async Task GetSchemaRawManyAsync_ItemTheBatchLeavesOut_IsA404()
    {
        // Arrange: the batch returns only the first; the second's own read is a 404.
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/batch"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{{Body(First)}}]}"""
            )
            .When(_ => true, HttpStatusCode.NotFound, """{"title":"Not found"}""");

        // Act
        var result = await Wire.Client(handler)
            .GetSchemaRawManyAsync(EntityKind.DataType, [First, Second]);

        // Assert
        Assert.Equal((false, 404), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task GetSchemaRawManyAsync_KindWithoutABatchEndpoint_ReadsEachId()
    {
        // Arrange
        var handler = Wire.Routed(
            ($"template/{First}", Body(First)),
            ($"template/{Second}", Body(Second))
        );

        // Act
        var result = await Wire.Client(handler)
            .GetSchemaRawManyAsync(EntityKind.Template, [First, Second]);

        // Assert
        Assert.Equal(
            (true, 2, 0),
            (
                result.IsSuccess,
                handler.Recordings.Count,
                handler.Recordings.Count(r => r.Uri.AbsolutePath.EndsWith("/batch"))
            )
        );
    }

    [Fact]
    public async Task GetSchemaRawManyAsync_ServerWithoutBatchEndpoints_ReadsEachIdAfterOne404()
    {
        // Arrange: Umbraco 17.0 to 17.2 route the batch path to nothing.
        var handler = new RoutingHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/batch"), HttpStatusCode.NotFound, "")
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/{First}"),
                HttpStatusCode.OK,
                Body(First)
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/{Second}"),
                HttpStatusCode.OK,
                Body(Second)
            );
        var client = Wire.Client(handler);

        // Act: two reads, so the second shows the 404 is remembered.
        var first = await client.GetSchemaRawManyAsync(EntityKind.MediaType, [First]);
        var second = await client.GetSchemaRawManyAsync(EntityKind.MemberType, [Second]);

        // Assert
        Assert.Equal(
            (true, true, 1, 3),
            (
                first.IsSuccess,
                second.IsSuccess,
                handler.Recordings.Count(r => r.Uri.AbsolutePath.EndsWith("/batch")),
                handler.Recordings.Count
            )
        );
    }

    [Fact]
    public async Task GetSchemaRawManyAsync_BatchFailsWithAServerError_IsAFailure()
    {
        // Arrange
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.InternalServerError,
            """{"title":"boom"}"""
        );

        // Act
        var result = await Wire.Client(handler)
            .GetSchemaRawManyAsync(EntityKind.DocumentType, [First]);

        // Assert
        Assert.Equal((false, 500), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task GetUserGroupsRawAsync_ReturnsEachListItemVerbatim()
    {
        // Arrange
        var handler = Wire.Routed(
            (
                "/user-group",
                $$"""{"total":2,"items":[{"id":"{{First}}","alias":"admin","sections":["content"]},{"id":"{{Second}}","alias":"writer"}]}"""
            )
        );

        // Act
        var result = await Wire.Client(handler).GetUserGroupsRawAsync(CancellationToken.None);

        // Assert
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse($$"""{"id":"{{First}}","alias":"admin","sections":["content"]}"""),
                result.Data![0]
            ),
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task GetUserGroupsRawAsync_FailedPage_IsAFailureNotAShortList()
    {
        // Arrange
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.InternalServerError,
            """{"title":"boom"}"""
        );

        // Act
        var result = await Wire.Client(handler).GetUserGroupsRawAsync(CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
    }
}
