using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Webhook get, update, delivery log and name resolution on the wire (#237). The update is a
/// read-merge-write, because <c>PUT webhook/{id}</c> takes the whole webhook and resets whatever
/// is left out; these tests pin what the <c>PUT</c> body keeps.
/// </summary>
public class WebhookWireTests
{
    private static readonly Guid HookId = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");
    private static readonly Guid BlogPost = Guid.Parse("1a2b3c4d-1234-5678-abcd-ef0123456789");

    /// <summary>A <c>GET webhook/{id}</c> body with every field the merge must keep.</summary>
    private const string Current = """
        {
          "id": "3f7a8b2e-1234-5678-abcd-ef0123456789",
          "name": "Deploy hook",
          "description": "Rebuilds the site",
          "url": "https://my.app/hook",
          "enabled": true,
          "events": [
            { "eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish" }
          ],
          "contentTypeKeys": [ "1a2b3c4d-1234-5678-abcd-ef0123456789" ],
          "headers": { "X-Api-Key": "old" }
        }
        """;

    /// <summary>A <c>GET webhook/events</c> body, for the event check on update.</summary>
    private const string Events = """
        {
          "total": 2,
          "items": [
            { "eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish" },
            { "eventName": "Content Unpublished", "eventType": "Content", "alias": "Umbraco.ContentUnpublish" }
          ]
        }
        """;

    /// <summary>A handler serving the events list and the current webhook; writes succeed empty.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Existing() =>
        new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/webhook/events"),
                HttpStatusCode.OK,
                Events
            )
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, Current)
            .When(_ => true, HttpStatusCode.OK, "");

    /// <summary>Runs an update against <see cref="Existing"/> and returns the handler.</summary>
    /// <param name="request">The update request.</param>
    /// <returns>The handler, holding what was sent.</returns>
    private static async Task<RoutingHandler> Update(UpdateWebhookRequest request)
    {
        var handler = Existing();
        await Wire.Client(handler).UpdateWebhookAsync(HookId, request, CancellationToken.None);
        return handler;
    }

    [Fact]
    public async Task GetWebhookAsync_ById_ReadsTheWebhook()
    {
        var handler = Wire.Returning(Current);

        var result = await Wire.Client(handler).GetWebhookAsync(HookId, CancellationToken.None);

        Assert.Equal("Deploy hook", result.Data!.Name);
    }

    [Fact]
    public async Task GetWebhookAsync_ById_MapsTheHeaders()
    {
        var handler = Wire.Returning(Current);

        var result = await Wire.Client(handler).GetWebhookAsync(HookId, CancellationToken.None);

        Assert.Equal("old", result.Data!.Headers!["X-Api-Key"]);
    }

    [Fact]
    public async Task GetWebhookAsync_NotFound_Fails()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.NotFound, "");

        var result = await Wire.Client(handler).GetWebhookAsync(HookId, CancellationToken.None);

        Assert.Equal((false, 404), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task UpdateWebhookAsync_OnlyEnabledGiven_PutsTheRestUnchanged()
    {
        var handler = await Update(new UpdateWebhookRequest { Enabled = false });

        var body = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}");
        Assert.Equal(
            """{"contentTypeKeys":["1a2b3c4d-1234-5678-abcd-ef0123456789"],"description":"Rebuilds the site","enabled":false,"events":["Umbraco.ContentPublish"],"headers":{"X-Api-Key":"old"},"name":"Deploy hook","url":"https://my.app/hook"}""",
            body.ToJsonString()
        );
    }

    [Fact]
    public async Task UpdateWebhookAsync_HeaderGiven_ReplacesTheSameNameIgnoringCase()
    {
        var handler = await Update(
            new UpdateWebhookRequest
            {
                Headers = new Dictionary<string, string>
                {
                    ["x-api-key"] = "new",
                    ["X-Env"] = "live",
                },
            }
        );

        var headers = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["headers"]!.AsObject();
        Assert.Equal(
            ["X-Api-Key=new", "X-Env=live"],
            headers.Select(h => $"{h.Key}={h.Value}").Order()
        );
    }

    [Fact]
    public async Task UpdateWebhookAsync_Replace_SendsOnlyTheGivenHeaders()
    {
        var handler = await Update(
            new UpdateWebhookRequest
            {
                Replace = true,
                Headers = new Dictionary<string, string> { ["X-Env"] = "live" },
            }
        );

        var headers = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["headers"]!.AsObject();
        Assert.Equal(["X-Env=live"], headers.Select(h => $"{h.Key}={h.Value}"));
    }

    [Fact]
    public async Task UpdateWebhookAsync_ReplaceWithNothingGiven_ClearsHeadersAndTypes()
    {
        var handler = await Update(new UpdateWebhookRequest { Replace = true });

        var body = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}");
        Assert.Equal(
            (0, 0),
            (body["headers"]!.AsObject().Count, body["contentTypeKeys"]!.AsArray().Count)
        );
    }

    [Fact]
    public async Task UpdateWebhookAsync_ReplaceWithNoEventsGiven_KeepsTheEvents()
    {
        // A webhook with no events never fires, so --replace does not clear them.
        var handler = await Update(new UpdateWebhookRequest { Replace = true });

        var events = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["events"]!.AsArray();
        Assert.Equal(["Umbraco.ContentPublish"], events.Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateWebhookAsync_EventsGiven_ReplacesTheEvents()
    {
        var handler = await Update(
            new UpdateWebhookRequest { Events = ["Umbraco.ContentUnpublish"] }
        );

        var events = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["events"]!.AsArray();
        Assert.Equal(["Umbraco.ContentUnpublish"], events.Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateWebhookAsync_TypesGiven_ReplacesTheTypeFilter()
    {
        var other = Guid.NewGuid();

        var handler = await Update(new UpdateWebhookRequest { ContentTypeKeys = [other] });

        var keys = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["contentTypeKeys"]!;
        Assert.Equal([other.ToString()], keys.AsArray().Select(k => k!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateWebhookAsync_UnknownEvent_IsRefusedWithoutPutting()
    {
        var handler = Existing();

        var result = await Wire.Client(handler)
            .UpdateWebhookAsync(
                HookId,
                new UpdateWebhookRequest { Events = ["ContentPublished"] },
                CancellationToken.None
            );

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        handler.AssertNoRequest(HttpMethod.Put, $"/webhook/{HookId}");
    }

    [Fact]
    public async Task UpdateWebhookAsync_WebhookMissing_FailsWithoutPutting()
    {
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.NotFound, "")
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Wire.Client(handler)
            .UpdateWebhookAsync(
                HookId,
                new UpdateWebhookRequest { Enabled = false },
                CancellationToken.None
            );

        Assert.False(result.IsSuccess);
        handler.AssertNoRequest(HttpMethod.Put, $"/webhook/{HookId}");
    }

    [Fact]
    public async Task UpdateWebhookAsync_Success_ReturnsTheWebhookReadBack()
    {
        var handler = Existing();

        var result = await Wire.Client(handler)
            .UpdateWebhookAsync(
                HookId,
                new UpdateWebhookRequest { Enabled = false },
                CancellationToken.None
            );

        Assert.Equal(HookId, result.Data!.Id);
    }

    [Fact]
    public async Task CreateWebhookAsync_HeadersAndTypes_SendsThem()
    {
        var handler = Wire.Routed(("/webhook/events", Events));

        await Wire.Client(handler)
            .CreateWebhookAsync(
                new CreateWebhookRequest
                {
                    Url = "https://my.app/hook",
                    Events = ["Umbraco.ContentPublish"],
                    Headers = new Dictionary<string, string> { ["X-Api-Key"] = "abc" },
                    ContentTypeKeys = [BlogPost],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/webhook");
        Assert.Equal(
            ("abc", BlogPost.ToString()),
            (
                body["headers"]!["X-Api-Key"]!.GetValue<string>(),
                body["contentTypeKeys"]![0]!.GetValue<string>()
            )
        );
    }

    /// <summary>A <c>GET webhook/.../logs</c> body with one failed delivery.</summary>
    private const string Logs = """
        {
          "total": 1,
          "items": [
            {
              "key": "9c8b7a6d-1234-5678-abcd-ef0123456789",
              "webhookKey": "3f7a8b2e-1234-5678-abcd-ef0123456789",
              "statusCode": "InternalServerError (500)",
              "isSuccessStatusCode": false,
              "date": "2026-09-28T10:00:00+00:00",
              "url": "https://my.app/hook",
              "eventAlias": "Umbraco.ContentPublish",
              "retryCount": 2,
              "requestHeaders": "X-Api-Key: abc",
              "requestBody": "{}",
              "responseHeaders": "",
              "responseBody": "boom",
              "exceptionOccured": true
            }
          ]
        }
        """;

    [Fact]
    public async Task GetWebhookLogsAsync_WithAWebhook_ReadsThatWebhooksLog()
    {
        var handler = Wire.Returning(Logs);

        await Wire.Client(handler).GetWebhookLogsAsync(HookId, 5, 10, CancellationToken.None);

        var query = handler.QueryOf(HttpMethod.Get, $"/webhook/{HookId}/logs");
        Assert.Equal(("5", "10"), (query["skip"], query["take"]));
    }

    [Fact]
    public async Task GetWebhookLogsAsync_NoWebhook_ReadsEveryWebhooksLog()
    {
        var handler = Wire.Returning(Logs);

        await Wire.Client(handler).GetWebhookLogsAsync(null, 0, 100, CancellationToken.None);

        Assert.Equal("/umbraco/management/api/v1/webhook/logs", handler.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task GetWebhookLogsAsync_Entry_MapsTheDelivery()
    {
        var handler = Wire.Returning(Logs);

        var result = await Wire.Client(handler)
            .GetWebhookLogsAsync(HookId, 0, 100, CancellationToken.None);

        var log = Assert.Single(result.Data!.Items);
        Assert.Equal(
            (HookId, "InternalServerError (500)", false, true, 2, "boom"),
            (
                log.WebhookId,
                log.StatusCode,
                log.IsSuccessStatusCode,
                log.ExceptionOccurred,
                log.RetryCount,
                log.ResponseBody
            )
        );
    }

    [Fact]
    public async Task GetWebhookLogsAsync_ServerError_Fails()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.InternalServerError, "");

        var result = await Wire.Client(handler)
            .GetWebhookLogsAsync(HookId, 0, 100, CancellationToken.None);

        Assert.Equal(FailureCategory.ServerError, result.Category);
    }

    /// <summary>A <c>GET webhook</c> page with two webhooks, one unnamed.</summary>
    private const string List = """
        {
          "total": 2,
          "items": [
            { "id": "3f7a8b2e-1234-5678-abcd-ef0123456789", "name": "Deploy hook", "url": "https://my.app/hook", "enabled": true, "events": [] },
            { "id": "4e5f6a7b-1234-5678-abcd-ef0123456789", "url": "https://other.app/hook", "enabled": true, "events": [] }
          ]
        }
        """;

    [Fact]
    public async Task ResolveIdAsync_WebhookName_IsTheWebhooksId()
    {
        var handler = Wire.Returning(List);

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.Webhook, "deploy HOOK", CancellationToken.None);

        Assert.Equal(HookId, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownWebhookName_IsAnInvalidArgument()
    {
        var handler = Wire.Returning(List);

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.Webhook, "nope", CancellationToken.None);

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }

    private static readonly Guid AuthorDoc = Guid.Parse("5a5a5a5a-1234-5678-abcd-ef0123456789");
    private static readonly Guid AuthorMember = Guid.Parse("6b6b6b6b-1234-5678-abcd-ef0123456789");
    private static readonly Guid Image = Guid.Parse("7c7c7c7c-1234-5678-abcd-ef0123456789");

    /// <summary>
    /// A site with a document type <c>author</c>, a member type <c>author</c> and a media type
    /// <c>image</c>: each kind's tree, then each type's by-id read for its alias.
    /// </summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Types() =>
        Wire.Routed(
            (
                "tree/document-type/root",
                $$"""{"total":1,"items":[{"id":"{{AuthorDoc}}","name":"Author","isFolder":false}]}"""
            ),
            ($"document-type/{AuthorDoc}", $$"""{"id":"{{AuthorDoc}}","alias":"author"}"""),
            (
                "tree/media-type/root",
                $$"""{"total":1,"items":[{"id":"{{Image}}","name":"Image","isFolder":false}]}"""
            ),
            ($"media-type/{Image}", $$"""{"id":"{{Image}}","alias":"image","name":"Image"}"""),
            (
                "tree/member-type/root",
                $$"""{"total":1,"items":[{"id":"{{AuthorMember}}","name":"Author","isFolder":false}]}"""
            ),
            ($"member-type/{AuthorMember}", $$"""{"id":"{{AuthorMember}}","alias":"author"}""")
        );

    [Fact]
    public async Task ResolveWebhookTypesAsync_MediaTypeAlias_IsThatType()
    {
        var result = await Wire.Client(Types())
            .ResolveWebhookTypesAsync(["image"], CancellationToken.None);

        Assert.Equal([Image], result.Data!);
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_Guid_MakesNoRequest()
    {
        var handler = Types();

        await Wire.Client(handler)
            .ResolveWebhookTypesAsync([BlogPost.ToString()], CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_AliasOfTwoKinds_IsRefusedWithBothIds()
    {
        // The filter does not say which kind it holds, so the alias cannot be guessed.
        var result = await Wire.Client(Types())
            .ResolveWebhookTypesAsync(["author"], CancellationToken.None);

        Assert.Equal(
            (FailureCategory.InvalidArgument, true, true),
            (
                result.Category,
                result.ErrorMessage!.Contains(AuthorDoc.ToString()),
                result.ErrorMessage.Contains(AuthorMember.ToString())
            )
        );
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_UnknownAlias_IsAnInvalidArgument()
    {
        var result = await Wire.Client(Types())
            .ResolveWebhookTypesAsync(["nope"], CancellationToken.None);

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }
}
