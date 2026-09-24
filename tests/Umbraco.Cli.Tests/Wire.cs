using System.Collections.Specialized;
using System.Text.Json.Nodes;
using System.Web;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Shared plumbing for asserting what a client method actually puts on the wire (#187 Phase 2).
/// <para>
/// Three bugs shipped green past the previous style of assertion: #158 (an empty <c>schedule</c>
/// object that made publish a no-op), #178 (a missing <c>template</c> that deleted it), and #184
/// (a query parameter named <c>filter</c> instead of <c>memberGroupName</c>). None were visible
/// to a test that checked arguments on a fake, a substring of the body, or only that some request
/// reached a URL. So the helpers here deal in the <b>parsed body</b>, the <b>HTTP method</b> and
/// the <b>query string</b>, and every one of them <b>throws when nothing matched</b> rather than
/// returning a benign empty value - a predicate that matches nothing must fail the test, not
/// quietly satisfy an <c>Assert.DoesNotContain</c>.
/// </para>
/// </summary>
internal static class Wire
{
    /// <summary>Builds a client whose HTTP calls are answered by <paramref name="handler"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <returns>A client bound to the handler.</returns>
    public static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    /// <summary>Whether a request's path ends with <paramref name="suffix"/> (query ignored).</summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="suffix">The path suffix, e.g. <c>/document/{id}/publish</c>.</param>
    /// <returns>True when the path ends with the suffix.</returns>
    public static bool PathEnds(HttpRequestMessage request, string suffix) =>
        request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a request's path contains <paramref name="fragment"/> (query ignored).</summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="fragment">The path fragment.</param>
    /// <returns>True when the path contains the fragment.</returns>
    public static bool PathHas(HttpRequestMessage request, string fragment) =>
        request.RequestUri!.AbsolutePath.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The parsed JSON body of the first request matching method + path suffix, as an object.
    /// </summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The body parsed as a JSON object.</returns>
    /// <exception cref="InvalidOperationException">No request matched, or the body was not an object.</exception>
    public static JsonObject BodyOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) =>
        handler.BodyNodeOf(method, pathSuffix) as JsonObject
        ?? throw new InvalidOperationException(
            $"The body of {method} ...{pathSuffix} was not a JSON object."
        );

    /// <summary>
    /// The parsed JSON body of the first request matching method + path suffix, as any node
    /// (some endpoints take a bare array).
    /// </summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The parsed body.</returns>
    /// <exception cref="InvalidOperationException">No request matched, or the body was empty.</exception>
    public static JsonNode BodyNodeOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    )
    {
        var raw = handler.RawBodyOf(method, pathSuffix);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"{method} ...{pathSuffix} was sent with no body. {Describe(handler)}"
            );
        return JsonNode.Parse(raw)
            ?? throw new InvalidOperationException(
                $"The body of {method} ...{pathSuffix} was not valid JSON: {raw}"
            );
    }

    /// <summary>The raw body text of the first request matching method + path suffix.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The body text; empty when the request carried no body.</returns>
    /// <exception cref="InvalidOperationException">No request matched.</exception>
    public static string RawBodyOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    )
    {
        handler.RequireMatch(method, pathSuffix);
        return handler.BodyForFirst(r => r.Method == method && PathEnds(r, pathSuffix));
    }

    /// <summary>The URI of the first request matching method + path suffix.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The request URI.</returns>
    /// <exception cref="InvalidOperationException">No request matched.</exception>
    public static Uri UriOf(this RoutingHandler handler, HttpMethod method, string pathSuffix) =>
        handler.RequireMatch(method, pathSuffix);

    /// <summary>
    /// The parsed query string of the first request matching method + a path fragment. Use this
    /// for the #184 class of bug, where correctness lives entirely in a parameter name.
    /// </summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathFragment">A fragment of the expected path.</param>
    /// <returns>The parsed query parameters.</returns>
    /// <exception cref="InvalidOperationException">No request matched.</exception>
    public static NameValueCollection QueryOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathFragment
    )
    {
        var uri =
            handler.RequestFor(r => r.Method == method && PathHas(r, pathFragment))
            ?? throw new InvalidOperationException(
                $"No {method} request reached a path containing '{pathFragment}'. {Describe(handler)}"
            );
        return HttpUtility.ParseQueryString(uri.Query);
    }

    /// <summary>Asserts that no request matching method + path suffix was made.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The method that must not have been used.</param>
    /// <param name="pathSuffix">The path suffix that must not have been requested.</param>
    /// <exception cref="InvalidOperationException">A matching request was made.</exception>
    public static void AssertNoRequest(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    )
    {
        if (handler.RequestFor(r => r.Method == method && PathEnds(r, pathSuffix)) is not null)
            throw new InvalidOperationException(
                $"Expected no {method} to ...{pathSuffix}, but one was sent. {Describe(handler)}"
            );
    }

    /// <summary>Resolves the matching request URI, failing loudly when there is none.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The matching request URI.</returns>
    /// <exception cref="InvalidOperationException">No request matched.</exception>
    private static Uri RequireMatch(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) =>
        handler.RequestFor(r => r.Method == method && PathEnds(r, pathSuffix))
        ?? throw new InvalidOperationException(
            $"No {method} request was made to a path ending '{pathSuffix}'. {Describe(handler)}"
        );

    /// <summary>Lists what the client actually sent, so a missed match names the alternatives.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <returns>A human-readable list of the recorded requests.</returns>
    private static string Describe(RoutingHandler handler) =>
        handler.Requests.Count == 0
            ? "No requests were made at all."
            : "Requests made: " + string.Join(", ", handler.Requests.Select(u => u.PathAndQuery));
}
