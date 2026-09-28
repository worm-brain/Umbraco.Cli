namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// What the <see cref="MutationInterceptorHandler"/> does when it sees a state-changing
/// (POST/PUT/PATCH/DELETE) request. This is the shared seam for the "safety" features:
/// <list type="bullet">
/// <item><see cref="Execute"/> — normal behaviour: send the request (the default).</item>
/// <item><see cref="Preview"/> — <c>--dry-run</c> (#62): record the request instead of
/// sending it, so nothing is mutated.</item>
/// <item><see cref="Block"/> — <c>--readonly</c> (#69): refuse the request with an error,
/// so a read-only session can never write.</item>
/// </list>
/// </summary>
public enum MutationInterceptPolicy
{
    /// <summary>Send state-changing requests as normal.</summary>
    Execute,

    /// <summary>Record the request instead of sending it (<c>--dry-run</c>).</summary>
    Preview,

    /// <summary>Refuse state-changing requests with an error (<c>--readonly</c>).</summary>
    Block,
}

/// <summary>
/// Process-scoped holder for the active <see cref="MutationInterceptPolicy"/>. Registered as
/// a singleton and set once per invocation by <c>CommandContextFactory</c> from the parsed
/// global options. The CLI runs exactly one command per process, so a mutable singleton is
/// safe here and lets the policy reach the <see cref="MutationInterceptorHandler"/> (built by
/// <c>IHttpClientFactory</c>) without threading it through every call.
/// </summary>
public sealed class MutationInterceptState
{
    /// <summary>The policy in force for this invocation. Defaults to <see cref="MutationInterceptPolicy.Execute"/>.</summary>
    public MutationInterceptPolicy Policy { get; set; } = MutationInterceptPolicy.Execute;

    /// <summary>
    /// The writes captured under <see cref="MutationInterceptPolicy.Preview"/>, in the order the
    /// command would have sent them (#353). <c>CommandExecutor</c> renders them as the dry-run
    /// preview once the command's call has finished.
    /// </summary>
    public List<PreviewedRequest> Previewed { get; } = [];
}

/// <summary>
/// One write request captured under <c>--dry-run</c>, with its URL and body already redacted by
/// <see cref="SecretRedactor"/> (#352).
/// </summary>
/// <param name="Method">The HTTP method that would have been sent (e.g. <c>POST</c>).</param>
/// <param name="Url">The absolute request URL that would have been called.</param>
/// <param name="Body">The request body that would have been sent, or null for a body-less request (e.g. DELETE).</param>
public sealed record PreviewedRequest(string Method, string Url, string? Body);

/// <summary>
/// Thrown by <see cref="MutationInterceptorHandler"/> under <see cref="MutationInterceptPolicy.Block"/>
/// (<c>--readonly</c>) to refuse a state-changing request. Caught by <c>CommandExecutor</c>,
/// which reports a read-only error and a non-zero exit — the request never reaches the server.
/// </summary>
public sealed class ReadOnlyModeException : Exception
{
    /// <summary>The HTTP method that was refused (e.g. <c>POST</c>).</summary>
    public string Method { get; }

    /// <summary>The request URL that was refused.</summary>
    public string Url { get; }

    /// <summary>Creates the exception carrying the refused request.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The request URL.</param>
    public ReadOnlyModeException(string method, string url)
        : base($"Read-only mode: refusing {method} {url}")
    {
        Method = method;
        Url = url;
    }
}

/// <summary>
/// Intercepts state-changing HTTP requests according to the active
/// <see cref="MutationInterceptPolicy"/>. Under <see cref="MutationInterceptPolicy.Preview"/>
/// (<c>--dry-run</c>) a POST/PUT/PATCH/DELETE is recorded in
/// <see cref="MutationInterceptState.Previewed"/> and answered with a fake <c>204</c> instead of
/// being sent, so a multi-step write (a user create that then sets a password, #353) goes on to
/// record every request it would make, and nothing is mutated. Read requests (GET, HEAD,
/// OPTIONS) always pass through - including the reads a write flow makes first (e.g.
/// alias-to-id resolution), so the preview reflects the real mutations.
///
/// This handler is wired onto the Management-API clients only, never the auth client, so the
/// OAuth token exchange is unaffected.
/// </summary>
public sealed class MutationInterceptorHandler : DelegatingHandler
{
    private readonly MutationInterceptState _state;

    /// <summary>Creates the handler over the shared per-invocation policy state.</summary>
    /// <param name="state">The process-scoped policy holder.</param>
    public MutationInterceptorHandler(MutationInterceptState state) => _state = state;

    /// <summary>
    /// Applies the active policy to state-changing requests; everything else is forwarded
    /// unchanged.
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The inner handler's response for pass-through requests, or a fake success for a write
    /// recorded under <see cref="MutationInterceptPolicy.Preview"/>.
    /// </returns>
    /// <exception cref="ReadOnlyModeException">Under <see cref="MutationInterceptPolicy.Block"/> for a mutating request.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (_state.Policy == MutationInterceptPolicy.Execute || !IsMutating(request.Method))
            return await base.SendAsync(request, cancellationToken);

        // Block (--readonly): refuse the write outright.
        if (_state.Policy == MutationInterceptPolicy.Block)
            throw new ReadOnlyModeException(
                request.Method.Method,
                request.RequestUri?.ToString() ?? ""
            );

        // Preview (--dry-run): the media upload stages bytes to the temporary-file endpoint
        // before the media create. That staging is a prerequisite side-effect, not the operation
        // the user is previewing - and a dry run must stage nothing. So fake a success response
        // (without forwarding it, so no bytes leave the process) and let the subsequent media
        // create POST be the mutation that is actually previewed (#62 / ADR 0004).
        if (IsTemporaryFileStaging(request))
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created)
            {
                Content = new StringContent(""),
                RequestMessage = request,
            };

        // Preview: record the request, redacted by the same policy as -v (#352), and answer it
        // with an empty success so a multi-step write carries on to its next request (#353).
        // Nothing is sent. A step that needed the real response (a created id in a Location
        // header) fails instead, and the executor still previews what was recorded.
        var body = await CaptureBodyAsync(request.Content, cancellationToken);
        _state.Previewed.Add(
            new PreviewedRequest(
                request.Method.Method,
                SecretRedactor.RedactUrl(request.RequestUri?.ToString() ?? ""),
                body
            )
        );
        return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)
        {
            ReasonPhrase = "No Content (dry run: not sent)",
            RequestMessage = request,
        };
    }

    /// <summary>
    /// Whether a request is the temporary-file staging POST of the two-step media upload. Used to
    /// let that staging be faked (not forwarded) under <c>--dry-run</c> so the previewed mutation
    /// is the media create, not the file staging (#62 / ADR 0004).
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <returns>True for a POST to the <c>temporary-file</c> endpoint.</returns>
    private static bool IsTemporaryFileStaging(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post
        && (
            request.RequestUri?.AbsolutePath.EndsWith(
                "/temporary-file",
                StringComparison.OrdinalIgnoreCase
            ) ?? false
        );

    /// <summary>
    /// Renders the request body for the preview. Only textual bodies (JSON/text/form) are
    /// read in full; binary/multipart bodies (e.g. a media file upload) are summarised as a
    /// placeholder rather than decoded into a string — decoding a multi-GB file would risk
    /// OutOfMemory and dump binary noise into the output.
    /// </summary>
    /// <param name="content">The request content, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The textual body, a placeholder for binary content, or null when there is no body.</returns>
    private static async Task<string?> CaptureBodyAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null)
            return null;

        var mediaType = content.Headers.ContentType?.MediaType ?? "";
        var isTextual =
            mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

        // Redacted by the same policy as -v, so a preview in a CI log leaks nothing (#352).
        if (isTextual)
            return SecretRedactor.RedactBody(
                await content.ReadAsStringAsync(ct),
                content.Headers.ContentType
            );

        var length = content.Headers.ContentLength;
        var size = length is { } l ? $"; {l:N0} bytes" : "";
        return $"<{(string.IsNullOrEmpty(mediaType) ? "binary" : mediaType)}{size}>";
    }

    /// <summary>
    /// Whether an HTTP method changes server state. Fail-safe: anything that is not a known
    /// safe read (GET/HEAD/OPTIONS) is treated as mutating, so an unexpected verb is caught
    /// rather than let through. Shared so the later read-only (#69) and confirmation (#70)
    /// policies classify requests identically.
    /// </summary>
    /// <param name="method">The request method.</param>
    /// <returns>True for a state-changing method.</returns>
    public static bool IsMutating(HttpMethod method) =>
        method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options;
}
