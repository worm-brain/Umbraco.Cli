using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Imaging resource on <see cref="UmbracoManagementClient"/> (issue #121): resized image URL
/// generation. The API returns a bare array (not a paged envelope), so it maps to a plain list.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<MediaResizeUrlResponse>>> GetResizeUrlsAsync(
        IReadOnlyList<Guid> mediaIds,
        int? width = null,
        int? height = null,
        ImageResizeMode? mode = null,
        string? format = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var list = await _api.Umbraco.Management.Api.V1.Imaging.Resize.Urls.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Id = mediaIds.Select(id => (Guid?)id).ToArray();
                        if (width is { } w)
                            c.QueryParameters.Width = w;
                        if (height is { } h)
                            c.QueryParameters.Height = h;
                        if (mode is { } m)
                            c.QueryParameters.Mode = ToGenCropMode(m);
                        if (!string.IsNullOrEmpty(format))
                            c.QueryParameters.Format = format;
                    },
                    ct
                );
                return (IReadOnlyList<MediaResizeUrlResponse>)
                    (list ?? [])
                        .Select(m => new MediaResizeUrlResponse
                        {
                            Id = m.Id ?? Guid.Empty,
                            Urls = (m.UrlInfos ?? [])
                                .Select(u => new MediaResizeUrl
                                {
                                    Culture = u.Culture,
                                    Url = u.Url,
                                })
                                .ToList(),
                        })
                        .ToList();
            }
        );

    /// <summary>Maps a command-facing resize mode to the generated crop-mode enum.</summary>
    /// <param name="mode">The command-facing mode.</param>
    /// <returns>The equivalent <see cref="Gen.ImageCropModeModel"/>.</returns>
    private static Gen.ImageCropModeModel ToGenCropMode(ImageResizeMode mode) =>
        mode switch
        {
            ImageResizeMode.Crop => Gen.ImageCropModeModel.Crop,
            ImageResizeMode.Max => Gen.ImageCropModeModel.Max,
            ImageResizeMode.Stretch => Gen.ImageCropModeModel.Stretch,
            ImageResizeMode.Pad => Gen.ImageCropModeModel.Pad,
            ImageResizeMode.BoxPad => Gen.ImageCropModeModel.BoxPad,
            ImageResizeMode.Min => Gen.ImageCropModeModel.Min,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown resize mode."),
        };
}
