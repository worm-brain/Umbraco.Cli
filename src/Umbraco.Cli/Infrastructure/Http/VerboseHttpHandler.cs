using System.Diagnostics;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// Logs each HTTP request and response (method, URI, headers, status, elapsed time) to
/// stderr — kept off stdout so machine-readable output stays clean. Wired onto the named
/// "umbraco-verbose" client and only selected when <c>--verbose</c> is set, so normal runs
/// are unaffected. The <c>Authorization</c> header is redacted so the bearer token never
/// leaks into logs.
/// </summary>
public sealed class VerboseHttpHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Console.Error.WriteLine($"> {request.Method} {request.RequestUri}");
        foreach (var (name, values) in request.Headers)
            Console.Error.WriteLine($"> {name}: {Redact(name, values)}");

        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();

        Console.Error.WriteLine(
            $"< {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds} ms)"
        );
        return response;
    }

    private static string Redact(string name, IEnumerable<string> values) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            ? "[redacted]"
            : string.Join(", ", values);
}
