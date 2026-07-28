namespace Umbraco.Cli.Client;

/// <summary>Media type read, create, and delete (issue #55 — parity with the MCP's media-type tools).</summary>
public interface IMediaTypeClient
{
    /// <summary>Lists media types from the media-type tree root.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of media types mapped to <see cref="MediaTypeResponse"/>.</returns>
    Task<UmbracoResponse<PagedResponse<MediaTypeResponse>>> GetMediaTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Gets a single media type by id, including its alias and description.</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The media type mapped to <see cref="MediaTypeResponse"/>.</returns>
    Task<UmbracoResponse<MediaTypeResponse>> GetMediaTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a media type.</summary>
    /// <param name="request">The media type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created media type (with the generated id), or a mapped failure.</returns>
    Task<UmbracoResponse<MediaTypeResponse>> CreateMediaTypeAsync(
        CreateMediaTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a media type by id.</summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteMediaTypeAsync(Guid id, CancellationToken ct = default);
}
