using System.Net;
using System.Text;

namespace Umbraco.Cli.Tests;

/// <summary>
/// One request as it left the client: the method, the URI and the body, captured before the
/// underlying <see cref="HttpRequestMessage"/> is disposed.
/// </summary>
/// <param name="Method">The HTTP method used.</param>
/// <param name="Uri">The absolute request URI, including any query string.</param>
/// <param name="Body">The request body, or null when the request carried none.</param>
internal sealed record Recorded(HttpMethod Method, Uri Uri, string? Body);

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that answers each request with the first matching
/// canned response and records what was sent. Unlike a single-body stub, this lets a multi-step
/// client flow (alias search -> get-by-id -> post -> hydrate) return a distinct body per step, so
/// both the requests and the mapping can be asserted.
/// </summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Match,
        HttpStatusCode Status,
        Func<HttpRequestMessage, string> Respond
    )> _routes = [];

    /// <summary>Every request the client made, in order.</summary>
    public List<Recorded> Recordings { get; } = [];

    /// <summary>Every request URI the client made, in order.</summary>
    public IReadOnlyList<Uri> Requests => [.. Recordings.Select(r => r.Uri)];

    /// <summary>Each request's body text, in order (null for bodiless requests).</summary>
    public IReadOnlyList<string?> RequestBodies => [.. Recordings.Select(r => r.Body)];

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
    ) => When(match, status, _ => json);

    /// <summary>
    /// Registers a route whose body is built from the request, for a fixture that answers by
    /// query (a tree page for a given parent, <c>skip</c> and <c>take</c>) rather than with one
    /// fixed body. The first registered route whose predicate matches a request wins.
    /// </summary>
    /// <param name="match">Predicate selecting the requests this route answers.</param>
    /// <param name="status">The HTTP status to return.</param>
    /// <param name="respond">Builds the JSON body for a matched request.</param>
    /// <returns>This handler, for chaining.</returns>
    public RoutingHandler When(
        Func<HttpRequestMessage, bool> match,
        HttpStatusCode status,
        Func<HttpRequestMessage, string> respond
    )
    {
        _routes.Add((match, status, respond));
        return this;
    }

    /// <summary>The first recorded request matching <paramref name="match"/>, or null.</summary>
    /// <param name="match">Predicate over the recorded requests.</param>
    /// <returns>The matching record, or null.</returns>
    public Recorded? FirstMatching(Func<Recorded, bool> match) => Recordings.FirstOrDefault(match);

    /// <summary>
    /// The first recorded request matching <paramref name="match"/>, failing the test when there
    /// is none.
    /// <para>
    /// Every post-hoc lookup goes through here so that a predicate matching nothing fails loudly.
    /// The earlier design returned an empty body instead, which quietly satisfied any
    /// "the body must not contain X" assertion against a request that was never made - a false
    /// green in exactly the tests meant to catch #158-class bugs (#187 Phase 2).
    /// </para>
    /// </summary>
    /// <param name="match">Predicate over the recorded requests.</param>
    /// <param name="what">How to describe the expectation in the failure message.</param>
    /// <returns>The matching record.</returns>
    /// <exception cref="WireAssertionException">No recorded request matched.</exception>
    public Recorded Require(Func<Recorded, bool> match, string what) =>
        FirstMatching(match)
        ?? throw new WireAssertionException(
            $"Expected a request {what}, but none was sent. {Describe()}"
        );

    /// <summary>The URI of the first request matching <paramref name="match"/>, or null.</summary>
    /// <param name="match">Predicate over the recorded requests.</param>
    /// <returns>The matching URI, or null.</returns>
    public Uri? RequestFor(Func<Recorded, bool> match) => FirstMatching(match)?.Uri;

    /// <summary>
    /// The body of the first request matching <paramref name="match"/>, failing when none did.
    /// </summary>
    /// <param name="match">Predicate over the recorded requests.</param>
    /// <returns>The body text; empty when the matched request carried none.</returns>
    /// <exception cref="WireAssertionException">No recorded request matched.</exception>
    public string BodyForFirst(Func<Recorded, bool> match) =>
        Require(match, "matching the predicate").Body ?? "";

    /// <summary>Lists what was actually sent, so a missed match names the alternatives.</summary>
    /// <returns>A human-readable list of the recorded requests.</returns>
    public string Describe() =>
        Recordings.Count == 0
            ? "No requests were made at all."
            : "Requests made: "
                + string.Join(", ", Recordings.Select(r => $"{r.Method} {r.Uri.PathAndQuery}"));

    /// <summary>
    /// Records the request and answers it from the first matching route (200 and an empty body
    /// when none matches).
    /// <para>
    /// Every request is also checked against <c>spec/management.json</c> (#76) with
    /// <see cref="ManagementSpec.AssertDeclared"/>: the contract test for the client's raw-JSON
    /// paths, whose URLs are strings the compiler cannot check. Deriving it from what the client
    /// sends means there is no endpoint list to keep in sync.
    /// </para>
    /// </summary>
    /// <param name="request">The request the client sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The canned response.</returns>
    /// <exception cref="WireAssertionException">
    /// The request is a Management API call the spec does not declare.
    /// </exception>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var recorded = new Recorded(
            request.Method,
            request.RequestUri!,
            request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
        );
        // Some reads fan out concurrently (type aliases, data-type hydration), and a List is not
        // safe to add to from several threads: an unlocked add can drop a request, which would
        // make a request count flaky.
        lock (Recordings)
            Recordings.Add(recorded);

        ManagementSpec.AssertDeclared(request);

        var route = _routes.FirstOrDefault(r => r.Match(request));
        var status = route.Match is null ? HttpStatusCode.OK : route.Status;
        var json = route.Respond?.Invoke(request) ?? "";
        return Task.FromResult(
            new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }
        );
    }
}
