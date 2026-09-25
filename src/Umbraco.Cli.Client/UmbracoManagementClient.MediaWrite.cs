using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The media write path (#220). There was no media update at all, so a custom media type's
/// optional fields could not be set after upload. <c>PUT /media/{id}</c> replaces the whole item,
/// so - as for content (#178) - the item is read verbatim and patched, never rebuilt from a typed
/// model that would drop what it does not know, the <c>umbracoFile</c> value included.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<MediaItemResponse>> UpdateMediaAsync(
        Guid id,
        UpdateMediaRequest request,
        bool replace = false,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var path = $"umbraco/management/api/v1/media/{id}";
                var media =
                    await GetRawJsonAsync(path, ct) as JsonObject
                    ?? throw new ApiException("The media body was not a JSON object.");

                // Media values and variants have the document shape, so the content merge applies.
                DocumentUpdateBody.Merge(media, request.Values, request.Variants, replace);
                await SendRawJsonAsync(Method.PUT, path, media, ct);

                var hydrated = await GetMediaByIdAsync(id, ct);
                return hydrated.IsSuccess && hydrated.Data is { } data
                    ? data
                    : new MediaItemResponse { Id = id };
            }
        );
}
