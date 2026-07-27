namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// What the <see cref="MutationInterceptorHandler"/> does when it sees a state-changing
/// (POST/PUT/PATCH/DELETE) request. This is the shared seam for the "safety" features:
/// <list type="bullet">
/// <item><see cref="Execute"/> — normal behaviour: send the request (the default).</item>
/// <item><see cref="Preview"/> — <c>--dry-run</c> (#62): capture the request and abort
/// before it is sent, so nothing is mutated.</item>
/// </list>
/// Further policies (a read-only <c>Block</c> for #69 and an interactive <c>Confirm</c> for
/// #70) will extend this enum when those features land; the handler is structured so they
/// slot in as new switch arms.
/// </summary>
public enum MutationInterceptPolicy
{
    /// <summary>Send state-changing requests as normal.</summary>
    Execute,

    /// <summary>Capture the request and abort before sending (<c>--dry-run</c>).</summary>
    Preview,
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
}

/// <summary>
/// Thrown by <see cref="MutationInterceptorHandler"/> under <see cref="MutationInterceptPolicy.Preview"/>
/// to abort a state-changing request before it is sent, carrying the exact wire details so
/// the command layer can print them. Caught by <c>CommandExecutor</c>, which renders the
/// dry-run preview and exits 0 — the request never reaches the server.
/// </summary>
public sealed class DryRunException : Exception
{
    /// <summary>The HTTP method that would have been sent (e.g. <c>POST</c>).</summary>
    public string Method { get; }

    /// <summary>The absolute request URL that would have been called.</summary>
    public string Url { get; }

    /// <summary>The request body that would have been sent, or null for a body-less request (e.g. DELETE).</summary>
    public string? Body { get; }

    /// <summary>Creates the exception carrying the captured request.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The absolute request URL.</param>
    /// <param name="body">The request body, or null when there is none.</param>
    public DryRunException(string method, string url, string? body)
        : base($"Dry run: {method} {url}")
    {
        Method = method;
        Url = url;
        Body = body;
    }
}

/// <summary>
/// Intercepts state-changing HTTP requests according to the active
/// <see cref="MutationInterceptPolicy"/>. Under <see cref="MutationInterceptPolicy.Preview"/>
/// (<c>--dry-run</c>) a POST/PUT/PATCH/DELETE is captured and aborted via
/// <see cref="DryRunException"/> instead of being sent, so agents and humans can see the
/// exact request a write command would make without mutating anything. Read requests (GET,
/// HEAD, OPTIONS) always pass through — including the reads a write flow makes first (e.g.
/// alias→id resolution), so the preview reflects the real final mutation.
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
    /// <returns>The inner handler's response for pass-through requests.</returns>
    /// <exception cref="DryRunException">Under <see cref="MutationInterceptPolicy.Preview"/> for a mutating request.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (_state.Policy == MutationInterceptPolicy.Execute || !IsMutating(request.Method))
            return await base.SendAsync(request, cancellationToken);

        // Preview: capture the request and abort before it is sent.
        var body = await CaptureBodyAsync(request.Content, cancellationToken);

        throw new DryRunException(
            request.Method.Method,
            request.RequestUri?.ToString() ?? "",
            body
        );
    }

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

        if (isTextual)
            return await content.ReadAsStringAsync(ct);

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
