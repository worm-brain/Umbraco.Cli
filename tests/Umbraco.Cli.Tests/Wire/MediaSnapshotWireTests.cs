using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the media snapshot client (#226) puts on the wire: files are downloaded from the site on
/// the configured host - and never from another host, which would receive the access token - and
/// staged through the temporary-file endpoint; the tree is walked in pre-order with parents.
/// </summary>
public class MediaSnapshotWireTests
{
    [Fact]
    public async Task DownloadMediaFileAsync_RelativeSrc_GetsItFromTheConfiguredHost()
    {
        var handler = Wire.Returning("jpeg bytes");
        using var output = new MemoryStream();

        var result = await Wire.Client(handler)
            .DownloadMediaFileAsync("/media/abc/photo.jpg", output, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Multiple(
            () =>
                Assert.Equal(
                    "https://example.com/media/abc/photo.jpg",
                    Assert.Single(handler.Requests).AbsoluteUri
                ),
            () => Assert.Equal("jpeg bytes", Encoding.UTF8.GetString(output.ToArray()))
        );
    }

    [Fact]
    public async Task DownloadMediaFileAsync_AnotherHost_IsRefusedWithoutARequest()
    {
        var handler = Wire.Blank();

        var result = await Wire.Client(handler)
            .DownloadMediaFileAsync(
                "https://cdn.example.net/media/abc/photo.jpg",
                new MemoryStream(),
                CancellationToken.None
            );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Empty(handler.Recordings)
        );
    }

    [Fact]
    public async Task StageTemporaryFileAsync_PostsTheFileToTemporaryFile()
    {
        var handler = Wire.Blank();

        var result = await Wire.Client(handler)
            .StageTemporaryFileAsync(
                new MemoryStream(Encoding.UTF8.GetBytes("jpeg bytes")),
                "photo.jpg",
                "image/jpeg",
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(
            result.Data.ToString(),
            handler.RawBodyOf(HttpMethod.Post, "/temporary-file")
        );
    }

    [Fact]
    public async Task GetMediaSnapshotTreeAsync_WalksChildrenWithTheirParent()
    {
        var folder = Guid.NewGuid();
        var image = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/tree/media/root"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{folder}}","hasChildren":true}]}"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/tree/media/children"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{image}}","hasChildren":false}]}"""
            );

        var result = await Wire.Client(handler)
            .GetMediaSnapshotTreeAsync(null, CancellationToken.None);

        Assert.Equal(
            new ContentTreeNode[] { new(folder, null), new(image, folder) },
            result.Data!.ToArray()
        );
    }
}
