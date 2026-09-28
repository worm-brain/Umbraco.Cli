using System.Net;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the <see cref="MutationInterceptorHandler"/> that backs <c>--dry-run</c> (#62):
/// under the Preview policy a state-changing request is recorded (redacted) and answered with a
/// fake success instead of being sent; everything else passes through untouched.
/// </summary>
public class MutationInterceptorHandlerTests
{
    /// <summary>Inner handler that records whether it was reached and returns 200.</summary>
    private sealed class OkHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, OkHandler Inner) Build(MutationInterceptPolicy policy) =>
        Build(new MutationInterceptState { Policy = policy });

    private static (HttpClient Client, OkHandler Inner) Build(MutationInterceptState state)
    {
        var inner = new OkHandler();
        var handler = new MutationInterceptorHandler(state) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        return (client, inner);
    }

    [Fact]
    public async Task Preview_MutatingRequest_RecordsTheCapturedDetails()
    {
        var state = new MutationInterceptState { Policy = MutationInterceptPolicy.Preview };
        var (client, _) = Build(state);

        await client.PostAsync(
            "umbraco/management/api/v1/webhook",
            new StringContent("""{"a":1}""", System.Text.Encoding.UTF8, "application/json")
        );

        var recorded = Assert.Single(state.Previewed);
        Assert.Equal(
            ("POST", "https://example.com/umbraco/management/api/v1/webhook", """{"a":1}"""),
            (recorded.Method, recorded.Url, recorded.Body)
        );
    }

    [Fact]
    public async Task Preview_MutatingRequest_IsNotSent()
    {
        var (client, inner) = Build(MutationInterceptPolicy.Preview);

        await client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("{}"));

        Assert.False(inner.WasCalled); // the request was never forwarded to the wire
    }

    [Fact]
    public async Task Preview_MutatingRequest_AnswersWithAFakeNoContent()
    {
        // A fake success lets a multi-step write go on to its next request (#353).
        var (client, _) = Build(MutationInterceptPolicy.Preview);

        var response = await client.PostAsync(
            "umbraco/management/api/v1/webhook",
            new StringContent("{}")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Preview_SeveralWrites_RecordsEachInOrder()
    {
        // #353: user create with --password, then its change-password step.
        var state = new MutationInterceptState { Policy = MutationInterceptPolicy.Preview };
        var (client, _) = Build(state);

        await client.PostAsync("umbraco/management/api/v1/user", new StringContent("{}"));
        await client.PostAsync(
            "umbraco/management/api/v1/user/1/change-password",
            new StringContent("{}")
        );

        Assert.Equal(
            [
                "/umbraco/management/api/v1/user",
                "/umbraco/management/api/v1/user/1/change-password",
            ],
            state.Previewed.Select(r => new Uri(r.Url).AbsolutePath)
        );
    }

    [Fact]
    public async Task Preview_JsonPassword_IsRedactedInTheRecordedBody()
    {
        // #352: the dry-run preview uses the same redaction as -v.
        var state = new MutationInterceptState { Policy = MutationInterceptPolicy.Preview };
        var (client, _) = Build(state);

        await client.PostAsync(
            "umbraco/management/api/v1/user/1/change-password",
            new StringContent(
                """{"newPassword":"NewSecret98765!"}""",
                System.Text.Encoding.UTF8,
                "application/json"
            )
        );

        Assert.DoesNotContain("NewSecret98765!", state.Previewed[0].Body);
    }

    [Fact]
    public async Task Preview_UrlQueryToken_IsRedactedInTheRecordedUrl()
    {
        var state = new MutationInterceptState { Policy = MutationInterceptPolicy.Preview };
        var (client, _) = Build(state);

        await client.PostAsync("umbraco/management/api/v1/x?token=abc123", new StringContent("{}"));

        Assert.DoesNotContain("abc123", state.Previewed[0].Url);
    }

    [Fact]
    public async Task Preview_ReadRequest_PassesThrough()
    {
        // A write flow can make reads first (alias->id resolution); those must still run so
        // the preview reflects the real final mutation.
        var (client, inner) = Build(MutationInterceptPolicy.Preview);

        var response = await client.GetAsync("umbraco/management/api/v1/language");

        Assert.True(inner.WasCalled);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preview_TemporaryFileStaging_FakedNotForwardedAndDoesNotThrow()
    {
        // #79/#62: the media upload stages bytes to temporary-file before the media create.
        // Under dry-run that staging must not be forwarded (nothing staged) yet must not abort
        // the flow either - so it is faked with a success response, letting the following media
        // create POST be the mutation that is actually previewed.
        var (client, inner) = Build(MutationInterceptPolicy.Preview);

        var response = await client.PostAsync(
            "umbraco/management/api/v1/temporary-file",
            new StringContent("multipart-body")
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(inner.WasCalled); // faked, not sent to the wire
    }

    [Fact]
    public async Task Preview_MediaCreateAfterStaging_IsThePreviewedMutation()
    {
        // The media create POST that follows the faked staging is captured as the dry-run
        // preview, exactly like any other write.
        var state = new MutationInterceptState { Policy = MutationInterceptPolicy.Preview };
        var (client, _) = Build(state);

        await client.PostAsync(
            "umbraco/management/api/v1/temporary-file",
            new StringContent("multipart-body")
        );
        await client.PostAsync(
            "umbraco/management/api/v1/media",
            new StringContent("""{"id":"x"}""")
        );

        Assert.EndsWith("/umbraco/management/api/v1/media", Assert.Single(state.Previewed).Url);
    }

    [Fact]
    public async Task Block_MutatingRequest_ThrowsReadOnlyAndDoesNotSend()
    {
        // #69: under Block (--readonly) a write is refused before it reaches the wire.
        var (client, inner) = Build(MutationInterceptPolicy.Block);

        var ex = await Assert.ThrowsAsync<ReadOnlyModeException>(() =>
            client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("{}"))
        );

        Assert.Equal("POST", ex.Method);
        Assert.EndsWith("/umbraco/management/api/v1/webhook", ex.Url);
        Assert.False(inner.WasCalled);
    }

    [Fact]
    public async Task Block_ReadRequest_PassesThrough()
    {
        // Read-only still allows reads.
        var (client, inner) = Build(MutationInterceptPolicy.Block);

        await client.GetAsync("umbraco/management/api/v1/language");

        Assert.True(inner.WasCalled);
    }

    [Fact]
    public async Task Execute_MutatingRequest_PassesThrough()
    {
        // Default policy: writes are sent as normal (no dry-run).
        var (client, inner) = Build(MutationInterceptPolicy.Execute);

        await client.PostAsync("umbraco/management/api/v1/webhook", new StringContent("{}"));

        Assert.True(inner.WasCalled);
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    public void IsMutating_ClassifiesMethods(string method, bool expected) =>
        Assert.Equal(expected, MutationInterceptorHandler.IsMutating(new HttpMethod(method)));
}
