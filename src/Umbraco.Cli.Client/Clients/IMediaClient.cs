namespace Umbraco.Cli.Client;

/// <summary>Media library read, upload, and delete.</summary>
public interface IMediaClient
{
    Task<UmbracoResponse<PagedResponse<MediaItemResponse>>> GetMediaAsync(
        Guid? parentId = null, int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<MediaItemResponse>> GetMediaByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<MediaItemResponse>> UploadMediaAsync(Guid parentId, string name, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);

    Task<UmbracoResponse<Empty>> DeleteMediaAsync(Guid id, CancellationToken ct = default);
}
