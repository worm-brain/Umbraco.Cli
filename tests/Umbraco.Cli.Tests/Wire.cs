using System.Collections.Specialized;
using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using Umbraco.Cli.Client;
using Xunit.Sdk;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A failed wire expectation. Derives from <see cref="XunitException"/> so it reads as an
/// assertion failure rather than an unexpected error, and so a test asserting the guard fired
/// cannot be satisfied by an <see cref="InvalidOperationException"/> thrown from anywhere else.
/// </summary>
/// <param name="message">What was expected, and what was actually sent.</param>
internal sealed class WireAssertionException(string message) : XunitException(message);

/// <summary>
/// Asserting what a client method actually puts on the wire (#187 Phase 2).
/// <para>
/// Three bugs shipped green past the previous style of assertion: #158 (an empty <c>schedule</c>
/// object that made publish a no-op), #178 (a missing <c>template</c> that deleted it), and #184
/// (a query parameter named <c>filter</c> instead of <c>memberGroupName</c>). None were visible
/// to a test that checked arguments on a fake, a substring of the body, or only that some request
/// reached a URL. So these helpers deal in the <b>parsed body</b>, the <b>HTTP method</b> and the
/// <b>query string</b>, matching on the request's path <b>suffix</b> throughout - a single rule,
/// so which helper you call never changes what "matched" means.
/// </para>
/// <para>
/// <see cref="RoutingHandler.Require"/> owns the no-match failure; nothing here re-checks it.
/// </para>
/// </summary>
internal static class Wire
{
    /// <summary>Builds a client whose HTTP calls are answered by <paramref name="handler"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <returns>A client bound to the handler.</returns>
    public static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    /// <summary>A handler that answers every request with 200 and an empty body.</summary>
    /// <returns>The handler.</returns>
    public static RoutingHandler Blank() =>
        new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");

    /// <summary>A handler that answers every request with 200 and <paramref name="json"/>.</summary>
    /// <param name="json">The response body.</param>
    /// <returns>The handler.</returns>
    public static RoutingHandler Returning(string json) =>
        new RoutingHandler().When(_ => true, HttpStatusCode.OK, json);

    /// <summary>
    /// A handler for a read-merge write: GET returns <paramref name="current"/>, every other
    /// request succeeds with an empty body. Route order matters - the GET route is registered
    /// first so it wins.
    /// </summary>
    /// <param name="current">The body the GET returns.</param>
    /// <returns>The handler.</returns>
    public static RoutingHandler Existing(string current) =>
        new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, current)
            .When(_ => true, HttpStatusCode.OK, "");

    /// <summary>
    /// A handler that answers each request with the body of the first route whose path fragment
    /// the request URI contains, falling back to 200 and an empty body.
    /// <para>
    /// A <c>GET {kind}/batch?id=..&amp;id=..</c> that no route names is answered from the by-id
    /// routes (fragments of the form <c>{kind}/{guid}</c>): <c>{"total", "items"}</c> with the
    /// body of each id that has one, as Umbraco's batch items equal its by-id bodies (#418). So a
    /// fixture states each type once, and holds whichever way the client reads it.
    /// </para>
    /// </summary>
    /// <param name="routes">Path fragment to response body, in precedence order.</param>
    /// <returns>The handler.</returns>
    public static RoutingHandler Routed(params (string Fragment, string Json)[] routes)
    {
        var handler = new RoutingHandler();
        foreach (var (fragment, json) in routes)
            handler.When(
                r =>
                    r.RequestUri!.AbsoluteUri.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase
                    ),
                HttpStatusCode.OK,
                json
            );
        return handler
            .When(
                r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/batch"),
                HttpStatusCode.OK,
                r => BatchFromByIdRoutes(r, routes)
            )
            .When(_ => true, HttpStatusCode.OK, "");
    }

    /// <summary>A batch response built from by-id routes; see <see cref="Routed"/>.</summary>
    /// <param name="request">The batch request.</param>
    /// <param name="routes">The fixture's routes.</param>
    /// <returns>The <c>{"total", "items"}</c> body.</returns>
    private static string BatchFromByIdRoutes(
        HttpRequestMessage request,
        (string Fragment, string Json)[] routes
    )
    {
        var segments = request.RequestUri!.AbsolutePath.Split('/');
        var kind = segments[^2];
        var ids = HttpUtility.ParseQueryString(request.RequestUri.Query).GetValues("id") ?? [];
        var items = new JsonArray();
        foreach (var id in ids)
        {
            var route = routes.FirstOrDefault(r =>
                r.Fragment.Equals($"{kind}/{id}", StringComparison.OrdinalIgnoreCase)
            );
            if (route.Json is not null)
                items.Add(JsonNode.Parse(route.Json));
        }
        return new JsonObject { ["total"] = items.Count, ["items"] = items }.ToJsonString();
    }

    /// <summary>Whether a recorded request's path ends with <paramref name="suffix"/>.</summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="suffix">The path suffix, e.g. <c>/document/{id}/publish</c>.</param>
    /// <returns>True when the path ends with the suffix.</returns>
    public static bool PathEnds(Recorded request, string suffix) =>
        request.Uri.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Asserts that a request was made, and returns it.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The matching request.</returns>
    /// <exception cref="WireAssertionException">No request matched.</exception>
    public static Recorded AssertRequested(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) =>
        handler.Require(
            r => r.Method == method && PathEnds(r, pathSuffix),
            $"{method} to a path ending '{pathSuffix}'"
        );

    /// <summary>Asserts that no request matching method + path suffix was made.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The method that must not have been used.</param>
    /// <param name="pathSuffix">The path suffix that must not have been requested.</param>
    /// <exception cref="WireAssertionException">A matching request was made.</exception>
    public static void AssertNoRequest(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    )
    {
        if (handler.FirstMatching(r => r.Method == method && PathEnds(r, pathSuffix)) is not null)
            throw new WireAssertionException(
                $"Expected no {method} to a path ending '{pathSuffix}', but one was sent. "
                    + handler.Describe()
            );
    }

    /// <summary>The parsed JSON body of the matching request, as an object.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The body parsed as a JSON object.</returns>
    /// <exception cref="WireAssertionException">No request matched, or the body was not an object.</exception>
    public static JsonObject BodyOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) =>
        handler.BodyNodeOf(method, pathSuffix) as JsonObject
        ?? throw new WireAssertionException(
            $"The body of {method} ...{pathSuffix} was not a JSON object."
        );

    /// <summary>
    /// The parsed JSON body of the matching request, as any node (some endpoints take a bare
    /// array).
    /// </summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The parsed body.</returns>
    /// <exception cref="WireAssertionException">No request matched, or the body was absent or invalid.</exception>
    public static JsonNode BodyNodeOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    )
    {
        var raw = handler.RawBodyOf(method, pathSuffix);
        if (string.IsNullOrWhiteSpace(raw))
            throw new WireAssertionException(
                $"{method} ...{pathSuffix} was sent with no body. {handler.Describe()}"
            );
        return JsonNode.Parse(raw)
            ?? throw new WireAssertionException(
                $"The body of {method} ...{pathSuffix} was not valid JSON: {raw}"
            );
    }

    /// <summary>The raw body text of the matching request.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The body text; empty when the request carried no body.</returns>
    /// <exception cref="WireAssertionException">No request matched.</exception>
    public static string RawBodyOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) => handler.AssertRequested(method, pathSuffix).Body ?? "";

    /// <summary>
    /// The parsed query string of the matching request. Use this for the #184 class of bug, where
    /// correctness lives entirely in a parameter name.
    /// </summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="method">The expected HTTP method.</param>
    /// <param name="pathSuffix">The expected path suffix.</param>
    /// <returns>The parsed query parameters.</returns>
    /// <exception cref="WireAssertionException">No request matched.</exception>
    public static NameValueCollection QueryOf(
        this RoutingHandler handler,
        HttpMethod method,
        string pathSuffix
    ) => HttpUtility.ParseQueryString(handler.AssertRequested(method, pathSuffix).Uri.Query);
}
