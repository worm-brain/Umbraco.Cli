using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>dictionary tree</c>'s client walk: a flat pre-order list over the dictionary hierarchy, each
/// item tagged with its depth and parent, stopping at the requested depth.
/// </summary>
public class DictionaryWalkClientTests
{
    private static readonly Guid Blog = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BlogTitle = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Home = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A root with Blog (one child, BlogTitle) and Home (no children).</summary>
    private static RoutingHandler TwoLevels() =>
        new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/tree/dictionary/root"),
                HttpStatusCode.OK,
                $$"""
                {"total":2,"items":[
                  {"id":"{{Blog}}","name":"Blog","hasChildren":true},
                  {"id":"{{Home}}","name":"Home","hasChildren":false}]}
                """
            )
            .When(
                r => r.RequestUri!.Query.Contains(Blog.ToString()),
                HttpStatusCode.OK,
                $$$"""
                {"total":1,"items":[
                  {"id":"{{{BlogTitle}}}","name":"Blog.Title","hasChildren":false,"parent":{"id":"{{{Blog}}}"}}]}
                """
            );

    [Fact]
    public async Task WalkDictionaryTreeAsync_Recursive_ListsEveryItemInPreOrderWithDepth()
    {
        var client = Wire.Client(TwoLevels());

        var result = await client.WalkDictionaryTreeAsync(
            null,
            int.MaxValue,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            [("Blog", 1, (Guid?)null), ("Blog.Title", 2, Blog), ("Home", 1, null)],
            result.Data!.Select(i => (i.Name, i.Depth, i.ParentId))
        );
    }

    [Fact]
    public async Task WalkDictionaryTreeAsync_DepthOne_DoesNotDescend()
    {
        var handler = TwoLevels();

        var result = await Wire.Client(handler)
            .WalkDictionaryTreeAsync(null, 1, CancellationToken.None);

        Assert.Equal(["Blog", "Home"], result.Data!.Select(i => i.Name));
    }

    [Fact]
    public async Task WalkDictionaryTreeAsync_ServerFails_IsAFailureNotAPartialList()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.InternalServerError, "");

        var result = await Wire.Client(handler)
            .WalkDictionaryTreeAsync(null, 5, CancellationToken.None);

        Assert.Equal(FailureCategory.ServerError, result.Category);
    }
}
