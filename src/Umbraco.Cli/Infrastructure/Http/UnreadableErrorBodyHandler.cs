using System.Text.Json;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Drops an error response's body when it is not readable JSON, keeping the status (#286).
/// <para>
/// Every 4xx/5xx body is read as Umbraco's ProblemDetails, and Kiota parses the body before any
/// error mapping runs, so an HTML page from a proxy (a 502 "Bad gateway", say) made the parse
/// itself throw - a crash reported as invalid input, with the status lost. Without a body, Kiota
/// takes its empty-body path and the client reports the status with its generic wording. Nothing
/// Umbraco said is lost: a body that does not parse as JSON is not a ProblemDetails.
/// </para>
/// </summary>
public sealed class UnreadableErrorBodyHandler : DelegatingHandler
{
    /// <summary>Sends the request and, for an error response, keeps its body only when it is JSON.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with an unreadable error body removed.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode < 400 || response.Content is null)
            return response;

        // Error bodies are small; buffering one to check it costs nothing next to the request.
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (body.Length == 0 || IsJson(body))
        {
            // ReadAsByteArrayAsync buffered the content, so Kiota can still read it.
            return response;
        }

        response.Content = new ByteArrayContent([]);
        return response;
    }

    /// <summary>Whether <paramref name="body"/> parses as a JSON document.</summary>
    /// <param name="body">The response body.</param>
    /// <returns>True when it is JSON.</returns>
    private static bool IsJson(byte[] body)
    {
        try
        {
            using var _ = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
