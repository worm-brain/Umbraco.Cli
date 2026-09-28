using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Logs each HTTP request and response (method, URI, headers, status, elapsed time, and the text
/// bodies) to stderr - kept off stdout so machine-readable output stays clean. Wired onto the named
/// "umbraco-verbose" client and only selected when <c>--verbose</c> is set, so normal runs are
/// unaffected.
/// <para>
/// Secrets never reach the log: the <c>Authorization</c> header is redacted, and so is any JSON
/// property or form field whose name looks like a credential (<see cref="IsSecretName"/>). The
/// token exchange does not go through this client at all, but a <c>client_secret</c> form field is
/// redacted here too, so a future wiring change cannot leak it. Binary and multipart bodies are
/// summarised by type and size, not printed, and a response body is cut at
/// <see cref="MaxResponseBodyChars"/> (#166).
/// </para>
/// </summary>
public sealed class VerboseHttpHandler : DelegatingHandler
{
    /// <summary>How much of a response body is printed before it is marked truncated.</summary>
    public const int MaxResponseBodyChars = 4096;

    /// <summary>The placeholder printed instead of a secret.</summary>
    private const string Redacted = "[redacted]";

    /// <summary>Logs the request, sends it, then logs the response.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with its content buffered so the caller can still read it.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Console.Error.WriteLine($"> {request.Method} {request.RequestUri}");
        foreach (var (name, values) in request.Headers)
            Console.Error.WriteLine($"> {name}: {RedactHeader(name, values)}");
        if (request.Content is not null)
            Console.Error.WriteLine(
                $"> {await DescribeBodyAsync(request.Content, limit: null, cancellationToken)}"
            );

        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();

        Console.Error.WriteLine(
            $"< {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds} ms)"
        );
        // An empty body (204, most writes) prints nothing rather than a blank line.
        if (response.Content is { Headers.ContentLength: not 0 } content)
        {
            var body = await DescribeBodyAsync(content, MaxResponseBodyChars, cancellationToken);
            if (body.Length > 0)
                Console.Error.WriteLine($"< {body}");
        }
        return response;
    }

    /// <summary>
    /// Renders a body for the log: redacted text for a text-like media type, or a one-line summary
    /// for anything binary or multipart. Buffers the content first, so it can still be sent (a
    /// request) or read by the caller (a response) afterwards.
    /// </summary>
    /// <param name="content">The body.</param>
    /// <param name="limit">The most characters to print, or null for all of it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text to log; empty when the body is empty.</returns>
    internal static async Task<string> DescribeBodyAsync(
        HttpContent content,
        int? limit,
        CancellationToken ct
    )
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        if (!IsText(mediaType))
        {
            // Do not buffer a file upload just to measure it; the header says how big it is.
            var size = content.Headers.ContentLength is { } length ? $", {length} bytes" : "";
            return $"[body not shown: {mediaType ?? "no content type"}{size}]";
        }

        await content.LoadIntoBufferAsync(ct);
        var text = await content.ReadAsStringAsync(ct);
        if (text.Length == 0)
            return "";

        text = Redact(text, content.Headers.ContentType);
        return limit is { } max && text.Length > max
            ? $"{text[..max]}... [truncated, {text.Length} chars in all]"
            : text;
    }

    /// <summary>
    /// Whether a media type is text that is worth printing: JSON (including
    /// <c>application/problem+json</c>), form fields, XML and <c>text/*</c>. A missing type is
    /// treated as binary, since nothing says it is safe to print.
    /// </summary>
    /// <param name="mediaType">The media type, or null.</param>
    /// <returns>True when the body should be printed.</returns>
    internal static bool IsText(string? mediaType) =>
        mediaType is not null
        && (
            mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase
            )
        );

    /// <summary>
    /// Whether a JSON property or form field name holds a credential: anything naming a password,
    /// secret, token or API key, ignoring case. Deliberately broad - a redacted non-secret costs a
    /// re-run without <c>-v</c>; a printed secret cannot be taken back.
    /// </summary>
    /// <param name="name">The property or field name.</param>
    /// <returns>True when its value must not be printed.</returns>
    internal static bool IsSecretName(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("api_key", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces credential values in a body: form fields by name, JSON properties by name at any
    /// depth. Other text is returned as it is.
    /// </summary>
    /// <param name="text">The body text.</param>
    /// <param name="contentType">The body's content type.</param>
    /// <returns>The body with secrets replaced by <c>[redacted]</c>.</returns>
    internal static string Redact(string text, MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType ?? "";
        if (
            mediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return string.Join(
                "&",
                text.Split('&')
                    .Select(pair =>
                    {
                        var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
                        return IsSecretName(name) ? $"{pair.Split('=', 2)[0]}={Redacted}" : pair;
                    })
            );

        if (!mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            return text;

        try
        {
            // Re-serialized only when something was redacted, so an ordinary body is printed
            // byte for byte as it was sent.
            var node = JsonNode.Parse(text);
            return RedactNode(node) ? node!.ToJsonString(LogJson) : text;
        }
        catch (JsonException)
        {
            // Not valid JSON despite its type: print it as sent, since there are no property names
            // to redact by.
            return text;
        }
    }

    // Readable in a terminal: no \uXXXX escaping of non-ASCII text or of < > in markup.
    private static readonly JsonSerializerOptions LogJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Replaces secret-named property values in a JSON tree, in place.</summary>
    /// <param name="node">The node to walk.</param>
    /// <returns>True when anything was redacted.</returns>
    private static bool RedactNode(JsonNode? node)
    {
        var redacted = false;
        switch (node)
        {
            case JsonObject obj:
                // ToList: the loop replaces values, which would otherwise invalidate the iterator.
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsSecretName(name) && value is not null)
                    {
                        obj[name] = Redacted;
                        redacted = true;
                    }
                    else
                        redacted |= RedactNode(value);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    redacted |= RedactNode(item);
                break;
        }
        return redacted;
    }

    /// <summary>Renders a header's values, hiding the bearer token.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="values">The header values.</param>
    /// <returns>The text to log.</returns>
    private static string RedactHeader(string name, IEnumerable<string> values) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            ? Redacted
            : string.Join(", ", values);
}
