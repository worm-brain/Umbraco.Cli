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
    /// <summary>
    /// Values shorter than this are not scrubbed by value: replacing a one- or two-character
    /// string everywhere would wreck the log, and no real credential is that short.
    /// </summary>
    private const int MinSecretLength = 4;

    // The credential values this process has sent (client secret, bearer token, API key headers).
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Whether <c>--verbose</c> was given. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Records a credential value the CLI holds, so <see cref="Scrub"/> hides it wherever it later
    /// appears in the log, whatever it is called there - a server that reflects the client secret
    /// under an innocent JSON name or in a plain-text error page (SEC-AUTH-001).
    /// </summary>
    /// <param name="value">The value; null, empty and very short values are ignored.</param>
    public void AddSecret(string? value)
    {
        if (value is null || value.Length < MinSecretLength)
            return;
        lock (_lock)
            _secrets.Add(value);
    }

    /// <summary>Replaces every recorded secret value in <paramref name="text"/> with the placeholder.</summary>
    /// <param name="text">Text about to be logged.</param>
    /// <returns>The text with no recorded secret left in it.</returns>
    public string Scrub(string text)
    {
        lock (_lock)
        {
            // Longest first, so a secret that contains another is replaced whole.
            foreach (var secret in _secrets.OrderByDescending(s => s.Length))
                text = text.Replace(secret, SecretRedactor.Placeholder, StringComparison.Ordinal);
        }
        return text;
    }
}

/// <summary>
/// Logs each HTTP request and response (method, URI, headers, status, elapsed time, and the text
/// bodies) to stderr - kept off stdout so machine-readable output stays clean. Wired onto every
/// HTTP client and gated by <see cref="VerboseState"/>, so it is a pass-through unless
/// <c>--verbose</c> is set.
/// <para>
/// Secrets never reach the log: URLs, request headers and bodies go through
/// <see cref="SecretRedactor"/>, the same policy <c>--dry-run</c> uses (#349, #352), and the
/// credential values the CLI has sent are scrubbed by value from every line, so a server that
/// echoes them back under another name or in plain text cannot print them. Binary and
/// multipart bodies are summarised by type and size, not printed, and a response body is cut at
/// <see cref="MaxResponseBodyChars"/> (#166).
/// </para>
/// </summary>
public sealed class VerboseHttpHandler : DelegatingHandler
{
    /// <summary>How much of a response body is printed before it is marked truncated.</summary>
    public const int MaxResponseBodyChars = 4096;

    private readonly VerboseState _state;

    /// <summary>Creates a handler that always logs (used directly by tests).</summary>
    public VerboseHttpHandler()
        : this(null) { }

    /// <summary>Creates a handler that logs only while <paramref name="state"/> is enabled.</summary>
    /// <param name="state">The per-run verbose switch, or null to always log.</param>
    public VerboseHttpHandler(VerboseState? state) =>
        _state = state ?? new VerboseState { Enabled = true };

    /// <summary>
    /// Logs the request, sends it, then logs the response; a pass-through when verbose is off.
    /// Credential values the request carries are recorded first (<see cref="RecordSecretsAsync"/>),
    /// and every logged line is scrubbed of them by value as well as redacted by name.
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with its content buffered so the caller can still read it.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (!_state.Enabled)
            return await base.SendAsync(request, cancellationToken);

        await RecordSecretsAsync(request, cancellationToken);

        Log($"> {request.Method} {SecretRedactor.RedactUrl(request.RequestUri?.ToString() ?? "")}");
        foreach (var (name, values) in request.Headers)
            Log($"> {name}: {SecretRedactor.RedactHeader(name, values)}");
        if (request.Content is not null)
            Log($"> {await DescribeBodyAsync(request.Content, limit: null, cancellationToken)}");

        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();

        Log($"< {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds} ms)");
        // An empty body (204, most writes) prints nothing rather than a blank line.
        if (response.Content is { Headers.ContentLength: not 0 } content)
        {
            // Scrub before cutting, so the cut cannot leave half a secret in the log.
            var body = _state.Scrub(await DescribeBodyAsync(content, null, cancellationToken));
            if (body.Length > MaxResponseBodyChars)
                body = $"{body[..MaxResponseBodyChars]}... [truncated, {body.Length} chars in all]";
            if (body.Length > 0)
                Log($"< {body}");
        }
        return response;
    }

    /// <summary>Writes one line to stderr with every recorded secret value scrubbed out.</summary>
    /// <param name="line">The line.</param>
    private void Log(string line) => Console.Error.WriteLine(_state.Scrub(line));

    /// <summary>
    /// Records the credential values a request carries in <see cref="VerboseState"/>: the
    /// Authorization header's credential (the bearer token), every value of a secret-named header,
    /// and every secret-named field of a form body (the token exchange's <c>client_secret</c>).
    /// From then on those values are hidden wherever they appear, including in a server's echo.
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the values are recorded.</returns>
    private async Task RecordSecretsAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _state.AddSecret(request.Headers.Authorization?.Parameter);
        foreach (var (name, values) in request.Headers)
        {
            if (SecretRedactor.IsSecretName(name))
                foreach (var value in values)
                    _state.AddSecret(value);
        }

        if (
            request.Content?.Headers.ContentType?.MediaType is { } mediaType
            && mediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            await request.Content.LoadIntoBufferAsync(ct);
            foreach (var pair in (await request.Content.ReadAsStringAsync(ct)).Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && SecretRedactor.IsSecretName(Decode(parts[0])))
                    _state.AddSecret(Decode(parts[1]));
            }
        }
    }

    /// <summary>Decodes one form-urlencoded name or value (<c>+</c> is a space).</summary>
    /// <param name="text">The encoded text.</param>
    /// <returns>The decoded text.</returns>
    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

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
