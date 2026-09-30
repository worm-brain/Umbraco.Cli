using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The guarded raw passthrough behind <c>umbraco api</c> (ADR 0010). It is built on the same
/// Kiota adapter as the schema pipeline's raw-JSON helpers, so the request goes through the
/// CLI's HTTP pipeline (bearer token, 401 refresh, <c>--readonly</c> / <c>--dry-run</c>
/// interceptor) and an error body is read as ProblemDetails like any other failure.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> SendRawAsync(
        HttpMethod method,
        string path,
        JsonNode? body = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                if (PassthroughPath.Problem(path) is { } problem)
                    throw new InvalidArgumentException(problem);

                var uri = RawUri(path.TrimStart('/'));
                // Belt and braces for the string rule: whatever the URL normalised to, it must
                // still be under the host's own /umbraco/ (the host may carry a path prefix).
                var root = new Uri(_adapter.BaseUrl + "/").AbsolutePath;
                if (
                    !uri.AbsolutePath.StartsWith(
                        root + "umbraco/",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new InvalidArgumentException(
                        $"'{path}' does not resolve to a path under /umbraco/ on the site."
                    );

                var request = new RequestInformation
                {
                    HttpMethod = KiotaMethod(method),
                    URI = uri,
                };
                request.Headers.TryAdd("Accept", "application/json");
                // The caller chose this path, so the contract tests (ADR 0001) do not hold the
                // client to the spec for it; see PassthroughRequestOption.
                request.AddRequestOptions([PassthroughRequestOption.Instance]);
                if (body is not null)
                    request.SetStreamContent(
                        new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString())),
                        "application/json"
                    );

                var stream = await _adapter.SendPrimitiveAsync<Stream>(
                    request,
                    RawErrorMapping,
                    ct
                );
                return await ReadPassthroughBodyAsync(stream, ct);
            }
        );

    /// <summary>The Kiota method for one of the five methods the passthrough sends.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <returns>The matching Kiota method.</returns>
    /// <exception cref="InvalidArgumentException">Any other method.</exception>
    private static Method KiotaMethod(HttpMethod method) =>
        method.Method.ToUpperInvariant() switch
        {
            "GET" => Method.GET,
            "POST" => Method.POST,
            "PUT" => Method.PUT,
            "PATCH" => Method.PATCH,
            "DELETE" => Method.DELETE,
            _ => throw new InvalidArgumentException(
                $"'{method.Method}' is not a method the passthrough sends: use GET, POST, PUT, "
                    + "PATCH or DELETE."
            ),
        };

    /// <summary>
    /// Reads a passthrough response body for the envelope's <c>data</c>. Every success has data
    /// (docs/conventions.md 6.2), so a response with no body (a 201 create, a 204, a
    /// <c>--dry-run</c> answer) is <c>{}</c>; a body that is not JSON is kept as a JSON string
    /// rather than failing a request the server accepted.
    /// </summary>
    /// <param name="stream">The response content; null when Kiota saw a 204.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The body as JSON.</returns>
    private static async Task<JsonNode> ReadPassthroughBodyAsync(
        Stream? stream,
        CancellationToken ct
    )
    {
        if (stream is null)
            return new JsonObject();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }
}

/// <summary>
/// Marks a request <see cref="UmbracoManagementClient.SendRawAsync"/> sends on a caller's behalf.
/// Kiota copies request options onto the <see cref="HttpRequestMessage"/>, keyed by the option's
/// type name, which is how the test handlers tell a passthrough request from one the client built.
/// Only <c>SendRawAsync</c> may set it: it is the one exemption from the contract tests (ADR 0001),
/// and on any other request it would hide endpoint drift.
/// </summary>
internal sealed class PassthroughRequestOption : IRequestOption
{
    /// <summary>The shared instance; the option carries no state.</summary>
    internal static readonly PassthroughRequestOption Instance = new();

    /// <summary>The key Kiota stores the option under on the outgoing request.</summary>
    private static readonly HttpRequestOptionsKey<IRequestOption> Key = new(
        typeof(PassthroughRequestOption).FullName!
    );

    /// <summary>Whether <paramref name="request"/> was sent by the passthrough.</summary>
    /// <param name="request">An outgoing request, as a test handler receives it.</param>
    /// <returns>True for a passthrough request.</returns>
    internal static bool IsOn(HttpRequestMessage request) =>
        request.Options.TryGetValue(Key, out _);
}
