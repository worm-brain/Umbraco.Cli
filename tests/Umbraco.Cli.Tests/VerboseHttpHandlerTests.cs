using System.Net;
using System.Text;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>-v</c> logs request and response bodies to stderr (#166), with secrets redacted, binary
/// bodies summarised and long responses truncated.
/// </summary>
[Collection("ConsoleCapture")]
public class VerboseHttpHandlerTests
{
    /// <summary>Answers every request with a fixed response and remembers what it was sent.</summary>
    private sealed class Canned(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>The request body as the server received it.</summary>
        public string? ReceivedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Content is not null)
                ReceivedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return respond();
        }
    }

    /// <summary>Sends <paramref name="request"/> through the handler and returns what went to stderr.</summary>
    /// <param name="request">The request.</param>
    /// <param name="response">The canned response.</param>
    /// <param name="server">The inner handler, for tests that check what was sent.</param>
    /// <returns>The stderr log and the response the caller received.</returns>
    private static async Task<(string Log, HttpResponseMessage Response)> Send(
        HttpRequestMessage request,
        Func<HttpResponseMessage> response,
        Canned? server = null
    )
    {
        var handler = new VerboseHttpHandler { InnerHandler = server ?? new Canned(response) };
        using var invoker = new HttpMessageInvoker(handler);
        var original = Console.Error;
        using var err = new StringWriter();
        Console.SetError(err);
        try
        {
            var result = await invoker.SendAsync(request, CancellationToken.None);
            return (err.ToString(), result);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpRequestMessage Post(string body, string mediaType = "application/json") =>
        new(HttpMethod.Post, "https://x/umbraco/management/api/v1/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };

    [Fact]
    public async Task SendAsync_JsonRequest_LogsTheBody()
    {
        var (log, _) = await Send(Post("""{"name":"Hook"}"""), () => Json("{}"));

        Assert.Contains("""> {"name":"Hook"}""", log);
    }

    [Fact]
    public async Task SendAsync_JsonResponse_LogsTheBody()
    {
        var (log, _) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () => Json("""{"id":"abc"}""")
        );

        Assert.Contains("""< {"id":"abc"}""", log);
    }

    [Fact]
    public async Task SendAsync_RequestBody_IsStillSentAfterLogging()
    {
        var server = new Canned(() => Json("{}"));

        await Send(Post("""{"name":"Hook"}"""), () => Json("{}"), server);

        Assert.Equal("""{"name":"Hook"}""", server.ReceivedBody);
    }

    [Fact]
    public async Task SendAsync_ResponseBody_IsStillReadableByTheCaller()
    {
        var (_, response) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () => Json("""{"id":"abc"}""")
        );

        Assert.Equal("""{"id":"abc"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendAsync_JsonPassword_IsRedacted()
    {
        var (log, _) = await Send(
            Post("""{"user":{"email":"a@b.c","newPassword":"hunter2"}}"""),
            () => Json("{}")
        );

        Assert.DoesNotContain("hunter2", log);
    }

    [Fact]
    public async Task SendAsync_FormClientSecret_IsRedacted()
    {
        // The token exchange's shape. It goes through the plain client today; this guards a
        // future wiring change from printing the secret.
        var request = new HttpRequestMessage(HttpMethod.Post, "https://x/token")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "umbraco-back-office-cli",
                    ["client_secret"] = "s3cr3t-value",
                }
            ),
        };

        var (log, _) = await Send(request, () => Json("{}"));

        Assert.DoesNotContain("s3cr3t-value", log);
    }

    [Fact]
    public async Task SendAsync_TokenResponse_IsRedacted()
    {
        var (log, _) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () => Json("""{"access_token":"eyJhbGciOi","expires_in":300}""")
        );

        Assert.DoesNotContain("eyJhbGciOi", log);
    }

    [Fact]
    public async Task SendAsync_AuthorizationHeader_IsRedacted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://x/a");
        request.Headers.Authorization = new("Bearer", "bearer-token-value");

        var (log, _) = await Send(request, () => Json("{}"));

        Assert.DoesNotContain("bearer-token-value", log);
    }

    [Fact]
    public async Task SendAsync_LongResponse_IsTruncatedAndMarked()
    {
        var body = $$"""{"text":"{{new string('a', 10_000)}}"}""";

        var (log, _) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () => Json(body)
        );

        Assert.Contains($"[truncated, {body.Length} chars in all]", log);
    }

    [Fact]
    public async Task SendAsync_LongResponse_PrintsNoMoreThanTheLimit()
    {
        var body = $$"""{"text":"{{new string('a', 10_000)}}"}""";

        var (log, _) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () => Json(body)
        );

        Assert.DoesNotContain(new string('a', VerboseHttpHandler.MaxResponseBodyChars), log);
    }

    [Fact]
    public async Task SendAsync_OctetStreamResponse_IsSummarisedNotPrinted()
    {
        var (log, _) = await Send(
            new HttpRequestMessage(HttpMethod.Get, "https://x/a"),
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("RAWBYTES"))
                    {
                        Headers = { ContentType = new("application/octet-stream") },
                    },
                }
        );

        Assert.Contains("[body not shown: application/octet-stream, 8 bytes]", log);
    }

    [Fact]
    public async Task SendAsync_MultipartRequest_IsSummarisedNotPrinted()
    {
        var multipart = new MultipartFormDataContent
        {
            { new StringContent("FILECONTENTS"), "file", "a.txt" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://x/upload")
        {
            Content = multipart,
        };

        var (log, _) = await Send(request, () => Json("{}"));

        Assert.DoesNotContain("FILECONTENTS", log);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/problem+json", true)]
    [InlineData("text/plain", true)]
    [InlineData("application/x-www-form-urlencoded", true)]
    [InlineData("application/octet-stream", false)]
    [InlineData("multipart/form-data", false)]
    [InlineData("image/png", false)]
    [InlineData(null, false)]
    public void IsText_MediaType_ClassifiesPrintableBodies(string? mediaType, bool expected)
    {
        Assert.Equal(expected, VerboseHttpHandler.IsText(mediaType));
    }

    [Fact]
    public void Redact_InvalidJson_ReturnsTextUnchanged()
    {
        var result = VerboseHttpHandler.Redact("{not json", new("application/json"));

        Assert.Equal("{not json", result);
    }
}
