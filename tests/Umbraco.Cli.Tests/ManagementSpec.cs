using System.Text.Json;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The operations the committed OpenAPI document (<c>spec/management.json</c>) declares, and
/// whether a concrete request is one of them (#52, #76).
/// <para>
/// <see cref="RoutingHandler"/> checks every Management API request a client test sends against
/// this, so the contract is derived from what the client actually puts on the wire rather than
/// from a hand-kept list of endpoints.
/// </para>
/// </summary>
internal static class ManagementSpec
{
    /// <summary>The path prefix every Management API operation shares.</summary>
    public const string ApiPrefix = "/umbraco/management/api/";

    /// <summary>Every declared operation, as (upper-case method, path template segments).</summary>
    private static readonly Lazy<IReadOnlyList<(string Method, string[] Segments)>> Operations =
        new(Load);

    /// <summary>How many operations the spec declares; zero means it was not read.</summary>
    public static int OperationCount => Operations.Value.Count;

    /// <summary>
    /// Whether the spec declares an operation that a request with <paramref name="method"/> and
    /// <paramref name="absolutePath"/> would reach. A template segment such as <c>{name}</c>
    /// matches any one segment, except <c>{id}</c>, which only matches a GUID - so a typo'd
    /// literal such as <c>document/urlz</c> is not mistaken for <c>document/{id}</c>.
    /// </summary>
    /// <param name="method">The request's HTTP method.</param>
    /// <param name="absolutePath">The request's path, without the query string.</param>
    /// <returns>True when a declared operation matches.</returns>
    public static bool Declares(HttpMethod method, string absolutePath)
    {
        var segments = absolutePath.Trim('/').Split('/');
        return Operations.Value.Any(op =>
            op.Method == method.Method.ToUpperInvariant()
            && op.Segments.Length == segments.Length
            && op.Segments.Zip(segments).All(pair => SegmentMatches(pair.First, pair.Second))
        );
    }

    /// <summary>
    /// Fails when <paramref name="request"/> is a Management API call the spec does not declare.
    /// Test HTTP handlers call this on every request they answer, which makes every client test a
    /// contract test. Requests outside the Management API (another host's token endpoint, an
    /// arbitrary test URL) are not checked.
    /// </summary>
    /// <param name="request">The request a client sent.</param>
    /// <exception cref="WireAssertionException">
    /// The spec declares no such operation. Thrown rather than answered, so it surfaces as a
    /// test failure instead of an HTTP error the client would map.
    /// </exception>
    public static void AssertDeclared(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (
            path.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase)
            && !Declares(request.Method, path)
        )
            throw new WireAssertionException(
                $"The client sent {request.Method} {path}, which spec/management.json does not "
                    + "declare: the endpoint drifted, or the client builds the URL wrongly."
            );
    }

    /// <summary>Whether one template segment matches one request segment.</summary>
    /// <param name="template">The spec's segment, literal or <c>{param}</c>.</param>
    /// <param name="actual">The request's segment.</param>
    /// <returns>True when they match.</returns>
    private static bool SegmentMatches(string template, string actual)
    {
        if (!template.StartsWith('{'))
            return string.Equals(template, actual, StringComparison.OrdinalIgnoreCase);
        if (template == "{id}")
            return Guid.TryParse(actual, out _);
        return actual.Length > 0;
    }

    /// <summary>
    /// Loads the spec and flattens its <c>paths</c> into operations, plus a POST to each OAuth
    /// <c>tokenUrl</c> its security schemes name: the token endpoint is declared there rather than
    /// under <c>paths</c>, and a test that runs a command on client credentials sends to it.
    /// </summary>
    /// <returns>Every (method, template segments) pair the spec declares.</returns>
    private static IReadOnlyList<(string, string[])> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindSpec()));
        var ops = new List<(string, string[])>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var verb in path.Value.EnumerateObject())
            ops.Add((verb.Name.ToUpperInvariant(), path.Name.Trim('/').Split('/')));
        var schemes = doc.RootElement.GetProperty("components").GetProperty("securitySchemes");
        foreach (var scheme in schemes.EnumerateObject())
        foreach (var flow in scheme.Value.GetProperty("flows").EnumerateObject())
            if (flow.Value.TryGetProperty("tokenUrl", out var tokenUrl))
                ops.Add(("POST", tokenUrl.GetString()!.Trim('/').Split('/')));
        return ops;
    }

    /// <summary>
    /// Locates <c>spec/management.json</c> by walking up from the test assembly's directory to
    /// the repo root (works regardless of the bin/Debug depth).
    /// </summary>
    /// <returns>The absolute path to the spec file.</returns>
    /// <exception cref="FileNotFoundException">The spec is in no ancestor directory.</exception>
    private static string FindSpec()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "spec", "management.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate spec/management.json by walking up from " + AppContext.BaseDirectory
        );
    }
}
