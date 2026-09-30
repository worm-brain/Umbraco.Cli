using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// The raw request behind <c>umbraco api</c> (ADR 0010): a caller-chosen method and path on the
/// configured site, sent through the same HTTP pipeline as every other call, so the bearer token,
/// the 401 refresh, <c>--readonly</c> and <c>--dry-run</c> all apply to it.
/// </summary>
public interface IPassthroughClient
{
    /// <summary>
    /// Sends <paramref name="method"/> to <paramref name="path"/> on the configured host and returns
    /// the response body verbatim. The path must pass <see cref="PassthroughPath.Problem"/>, so the
    /// request can only reach the site's own <c>/umbraco/</c> routes, never another host.
    /// </summary>
    /// <param name="method">GET, POST, PUT, PATCH or DELETE.</param>
    /// <param name="path">
    /// The path from the host root, starting <c>/umbraco/</c>, with any query string. It need not be
    /// declared in the Management API spec: a package's own routes are allowed.
    /// </param>
    /// <param name="body">The JSON request body, sent as it is; null sends none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The response body as JSON; a body that is not JSON as a JSON string; <c>{}</c> when the
    /// response has no body. A 4xx/5xx is a failure carrying Umbraco's error body as its details,
    /// and a refused method or path is an <see cref="FailureCategory.InvalidArgument"/> failure
    /// with nothing sent.
    /// </returns>
    Task<UmbracoResponse<JsonNode>> SendRawAsync(
        HttpMethod method,
        string path,
        JsonNode? body = null,
        CancellationToken ct = default
    );
}
