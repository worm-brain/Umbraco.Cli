using System.Diagnostics;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Process-scoped switch for <c>--verbose</c>. Set once per invocation in <c>Program.cs</c> from
/// the parsed global options, so every HTTP client - the Management API client, the OAuth token
/// exchange and the <c>auth doctor</c> probes - logs through the same gated
/// <see cref="VerboseHttpHandler"/> (#374). The CLI runs one command per process, so a mutable
/// singleton is safe, as for <see cref="MutationInterceptState"/>.
/// </summary>
public sealed class VerboseState
{
    /// <summary>Whether <c>--verbose</c> was given. Off by default.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Logs each HTTP request and response (method, URI, headers, status, elapsed time, and the text
/// bodies) to stderr - kept off stdout so machine-readable output stays clean. Wired onto every
/// HTTP client and gated by <see cref="VerboseState"/>, so it is a pass-through unless
/// <c>--verbose</c> is set.
/// <para>
/// Secrets never reach the log: URLs, request headers and bodies go through
/// <see cref="SecretRedactor"/>, the same policy <c>--dry-run</c> uses (#349, #352). Binary and
/// multipart bodies are summarised by type and size, not printed, and a response body is cut at
/// <see cref="MaxResponseBodyChars"/> (#166).
/// </para>
/// </summary>
public sealed class VerboseHttpHandler : DelegatingHandler
{
    /// <summary>How much of a response body is printed before it is marked truncated.</summary>
    public const int MaxResponseBodyChars = 4096;

    private readonly VerboseState? _state;

    /// <summary>Creates a handler that always logs (used directly by tests).</summary>
    public VerboseHttpHandler()
        : this(null) { }

    /// <summary>Creates a handler that logs only while <paramref name="state"/> is enabled.</summary>
    /// <param name="state">The per-run verbose switch, or null to always log.</param>
    public VerboseHttpHandler(VerboseState? state) => _state = state;

    /// <summary>Logs the request, sends it, then logs the response; a pass-through when verbose is off.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with its content buffered so the caller can still read it.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (_state is { Enabled: false })
            return await base.SendAsync(request, cancellationToken);

        Console.Error.WriteLine(
            $"> {request.Method} {SecretRedactor.RedactUrl(request.RequestUri?.ToString() ?? "")}"
        );
        foreach (var (name, values) in request.Headers)
            Console.Error.WriteLine($"> {name}: {SecretRedactor.RedactHeader(name, values)}");
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

        text = SecretRedactor.RedactBody(text, content.Headers.ContentType);
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
}
