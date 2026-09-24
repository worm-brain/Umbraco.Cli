using System.Net;
using System.Text;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that answers each request with the first matching
/// canned response, and records every request URI + body. Unlike the single-body stub, this
/// lets a multi-step client flow (e.g. alias search -> get-by-id -> post -> hydrate) return a
/// distinct body per step so both the requests and the mapping can be asserted.
/// </summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Match,
        HttpStatusCode Status,
        string Json
    )> _routes = [];

    /// <summary>Every request URI the client made, in order.</summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>Each request's body text, in order (null for bodiless requests).</summary>
    public List<string?> RequestBodies { get; } = [];

    /// <summary>
    /// Registers a route: the first registered route whose predicate matches a request wins.
    /// </summary>
    /// <param name="match">Predicate selecting the requests this route answers.</param>
    /// <param name="status">The HTTP status to return.</param>
    /// <param name="json">The JSON body to return.</param>
    /// <returns>This handler, for chaining.</returns>
    public RoutingHandler When(
        Func<HttpRequestMessage, bool> match,
        HttpStatusCode status,
        string json
    )
    {
        _routes.Add((match, status, json));
        return this;
    }

    /// <summary>Returns the first recorded request URI matching <paramref name="match"/>, or null.</summary>
    /// <param name="match">Predicate over the recorded request URIs (by absolute URI string).</param>
    /// <returns>The matching URI, or null.</returns>
    public Uri? RequestFor(Func<HttpRequestMessage, bool> match)
    {
        var index = _matched.FindIndex(r => match(r));
        return index >= 0 ? Requests[index] : null;
    }

    /// <summary>
    /// Returns the captured body of the first request matching <paramref name="match"/>.
    /// <para>
    /// Throws when nothing matched rather than returning an empty string. Returning "" meant a
    /// predicate with a typo'd path satisfied every <c>Assert.DoesNotContain</c> and any
    /// "the body must not carry X" assertion silently passed against a request that was never
    /// made - a false green in exactly the tests meant to catch #158-class bugs (#187 Phase 2).
    /// A request that genuinely carried no body still returns "".
    /// </para>
    /// </summary>
    /// <param name="match">Predicate over the recorded requests.</param>
    /// <returns>The body text; empty when the matched request had no body.</returns>
    /// <exception cref="InvalidOperationException">No recorded request matched.</exception>
    public string BodyForFirst(Func<HttpRequestMessage, bool> match)
    {
        var index = _matched.FindIndex(r => match(r));
        if (index < 0)
            throw new InvalidOperationException(
                "No recorded request matched the predicate. "
                    + (
                        Requests.Count == 0
                            ? "No requests were made at all."
                            : "Requests made: "
                                + string.Join(", ", Requests.Select(u => u.PathAndQuery))
                    )
            );
        return RequestBodies[index] ?? "";
    }

    // The HttpRequestMessage is disposed after SendAsync, so a snapshot needed for later
    // predicate matching (method + uri) is retained here alongside the URI/body lists.
    private readonly List<HttpRequestMessage> _matched = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request.RequestUri!);
        RequestBodies.Add(
            request.Content is null
                ? null
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
        );
        // Retain a lightweight snapshot (method + uri) for post-hoc assertions; the original
        // request is disposed once this returns.
        _matched.Add(new HttpRequestMessage(request.Method, request.RequestUri));

        var route = _routes.FirstOrDefault(r => r.Match(request));
        var status = route.Match is null ? HttpStatusCode.OK : route.Status;
        var json = route.Json ?? "";
        return Task.FromResult(
            new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }
        );
    }
}
