using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

public class UmbracoManagementClientTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw _ex;
    }

    /// <summary>
    /// Test double that returns a canned JSON body + status for every request and records
    /// the last requested URI, so tests can assert both the endpoint the client called
    /// (the subject of issue #39) and how the response is mapped.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        private readonly string? _location;

        /// <summary>The absolute URI of the most recent request the client made.</summary>
        public Uri? LastRequestUri { get; private set; }

        /// <param name="json">The response body to return.</param>
        /// <param name="status">The HTTP status to return (defaults to 200 OK).</param>
        /// <param name="location">Optional Location response header (for create tests).</param>
        public StubHandler(
            string json,
            HttpStatusCode status = HttpStatusCode.OK,
            string? location = null
        )
        {
            _json = json;
            _status = status;
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastRequestUri = request.RequestUri;
            var message = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
            if (_location is not null)
                message.Headers.Location = new Uri(_location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(message);
        }
    }

    private static UmbracoManagementClient ClientThatThrows(Exception ex) =>
        new(
            new HttpClient(new ThrowingHandler(ex))
            {
                BaseAddress = new Uri("https://example.com/"),
            }
        );

    private static (UmbracoManagementClient Client, StubHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        string? location = null
    )
    {
        var handler = new StubHandler(json, status, location);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    [Fact]
    public async Task TransportFailure_BecomesFailureNotException()
    {
        var client = ClientThatThrows(new HttpRequestException("no such host"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("reach", result.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_BecomesFailure()
    {
        // HttpClient surfaces a timeout as TaskCanceledException with no caller cancellation.
        var client = ClientThatThrows(new TaskCanceledException("timeout"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task GenuineCancellation_Propagates()
    {
        var client = ClientThatThrows(new HttpRequestException("unused"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetContentByIdAsync(Guid.NewGuid(), cts.Token)
        );
    }

    [Fact]
    public async Task GetContentAsync_NoParent_CallsTreeRootEndpoint()
    {
        // Regression for #39: root listing must hit the document tree, not the flat
        // (non-existent) /document collection.
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tree/document/root", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetContentAsync_FlattensVariantNameAndPublishedState()
    {
        // Regression for #39/#42: display name and published flag live under variants[],
        // not at the top level, and must be surfaced on the mapped item.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "total": 1,
              "items": [
                {
                  "id": "{{id}}",
                  "createDate": "2024-01-02T03:04:05+00:00",
                  "documentType": { "id": "11111111-1111-1111-1111-111111111111" },
                  "variants": [ { "culture": null, "name": "Home", "state": "Published" } ]
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("Home", item.Name);
        Assert.True(item.IsPublished);
    }

    [Fact]
    public async Task GetLanguagesAsync_ParsesPagedShape()
    {
        // Regression for #41: GET /language returns a paged {total,items} object, not a
        // bare array, and must deserialize instead of throwing.
        var json = """
            {
              "total": 1,
              "items": [
                { "isoCode": "en-US", "name": "English", "isDefault": true, "isMandatory": false }
              ]
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetLanguagesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/umbraco/management/api/v1/language", handler.LastRequestUri!.AbsolutePath);
        var lang = Assert.Single(result.Data!);
        Assert.Equal("en-US", lang.IsoCode);
        Assert.True(lang.IsDefault);
    }

    [Fact]
    public async Task GetContentAsync_ApiError_BecomesFailure()
    {
        // A non-2xx from a generated call is surfaced as a failed response (status + no
        // throw), preserving the "errors are data" contract through the Kiota guard.
        var (client, _) = ClientReturning(
            """{"title":"Not Found"}""",
            HttpStatusCode.NotFound
        );

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task CreateWebhookAsync_201EmptyBody_IsSuccessWithIdFromLocation()
    {
        // Regression for #43: Umbraco returns 201 Created with an empty body; the create
        // must report success and surface the new id parsed from the Location header.
        var id = Guid.NewGuid();
        var (client, _) = ClientReturning(
            "",
            HttpStatusCode.Created,
            location: $"/umbraco/management/api/v1/webhook/{id}"
        );

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest { Url = "https://example.com/hook" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Data!.Id);
    }

    [Fact]
    public async Task Error_WithValidationErrors_SurfacesFieldNames()
    {
        // Regression for #48: a 400 ProblemDetails must surface the offending field, not
        // just the generic title.
        var json = """
            {
              "title": "One or more validation errors occurred.",
              "errors": { "$.icon": ["The Icon field is required."] }
            }
            """;
        var (client, _) = ClientReturning(json, HttpStatusCode.BadRequest);

        // A create uses the hand-written HttpClient path where BuildErrorAsync formats
        // the ProblemDetails errors map.
        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest { Url = "https://example.com/hook" },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("$.icon", result.ErrorMessage);
        Assert.Contains("Icon field is required", result.ErrorMessage);
    }

    [Fact]
    public async Task GetWebhooksAsync_ParsesEventsAsObjects()
    {
        // Regression for #46: the API returns events as objects, not strings, which used
        // to throw "cannot convert to System.String" once any webhook existed.
        var json = """
            {
              "total": 1,
              "items": [
                {
                  "id": "33333333-3333-3333-3333-333333333333",
                  "url": "https://example.com/hook",
                  "enabled": true,
                  "events": [
                    { "eventName": "ContentPublished", "eventType": "Other", "alias": "ContentPublished" }
                  ]
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetWebhooksAsync(0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var webhook = Assert.Single(result.Data!.Items);
        var evt = Assert.Single(webhook.Events!);
        Assert.Equal("ContentPublished", evt.EventName);
    }

    [Fact]
    public async Task GetDictionaryItemByKeyAsync_ResolvesHumanKeyToId()
    {
        // Regression for #44: a human key must be resolved to the item id (the endpoint is
        // keyed by GUID) rather than 404ing. The stub returns a list containing the key, so
        // the by-id GET should target the resolved id.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"total":1,"items":[{"id":"{{id}}","name":"Admin"}]}"""
        );

        var result = await client.GetDictionaryItemByKeyAsync("Admin", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/dictionary/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetDictionaryItemByKeyAsync_UnknownKey_Returns404()
    {
        // Regression for #44: an unmatched key yields a clear 404, not a silent empty item.
        var (client, _) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetDictionaryItemByKeyAsync("Nope", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void CreateDocumentTypeRequest_IncludesApiRequiredFields()
    {
        // Regression for #47: the payload must carry icon, the varies-by flags, the
        // cleanup object and the allowed-* collections, or Umbraco 400s the create.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new CreateDocumentTypeRequest { Name = "Widget", Alias = "widget" }
        );

        Assert.Contains("\"icon\":\"icon-document\"", json);
        Assert.Contains("\"variesByCulture\":false", json);
        Assert.Contains("\"cleanup\":", json);
        Assert.Contains("\"allowedTemplates\":", json);
    }

    [Fact]
    public async Task GetContentByIdAsync_FlattensVariantNameAndDates()
    {
        // Regression for #42: single-item GET carries name/dates under variants[], which
        // must be surfaced instead of an empty name and 0001-01-01 dates.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "documentType": { "id": "22222222-2222-2222-2222-222222222222" },
              "variants": [
                {
                  "culture": null,
                  "name": "Home",
                  "state": "Published",
                  "createDate": "2020-01-02T03:04:05+00:00",
                  "updateDate": "2021-02-03T04:05:06+00:00"
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetContentByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Home", result.Data!.Name);
        Assert.True(result.Data.IsPublished);
        Assert.Equal(2020, result.Data.CreateDate.Year);
    }

    [Fact]
    public async Task Error_EmptyBody_FallsBackToReasonPhrase()
    {
        // Regression for #48: a bare 404 (empty body) must not produce a blank message.
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
}
