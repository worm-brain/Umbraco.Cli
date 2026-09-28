using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Webhook event aliases (#234). Umbraco saves a webhook with an unknown event name and never
/// fires it, so a create must check its events against <c>GET webhook/events</c> and refuse the
/// unknown ones before anything is posted.
/// </summary>
public class WebhookEventsWireTests
{
    /// <summary>A trimmed <c>GET webhook/events</c> body with the core content events.</summary>
    private const string Events = """
        {
          "total": 3,
          "items": [
            { "eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish" },
            { "eventName": "Content Unpublished", "eventType": "Content", "alias": "Umbraco.ContentUnpublish" },
            { "eventName": "Media Saved", "eventType": "Media", "alias": "Umbraco.MediaSave" }
          ]
        }
        """;

    /// <summary>Creates a webhook subscribed to <paramref name="events"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <param name="events">The requested event aliases.</param>
    /// <returns>The create response.</returns>
    private static Task<UmbracoResponse<WebhookResponse>> Create(
        RoutingHandler handler,
        params string[] events
    ) =>
        Wire.Client(handler)
            .CreateWebhookAsync(
                new CreateWebhookRequest { Url = "https://example.com/hook", Events = events },
                CancellationToken.None
            );

    [Fact]
    public async Task CreateWebhookAsync_KnownAliases_PostsTheWebhook()
    {
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "Umbraco.ContentPublish", "Umbraco.MediaSave");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, handler.BodyOf(HttpMethod.Post, "/webhook")["events"]!.AsArray().Count);
    }

    [Fact]
    public async Task CreateWebhookAsync_UnknownAlias_IsRefusedWithoutPosting()
    {
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "Umbraco.ContentPublish", "ContentPublished");

        Assert.False(result.IsSuccess);
        handler.AssertNoRequest(HttpMethod.Post, "/webhook");
    }

    [Fact]
    public async Task CreateWebhookAsync_UnknownAlias_ReportsTheUnknownAndKnownAliases()
    {
        // #278: the client reports the values; the command layer builds the suggestion.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "Umbraco.ContentPublish", "ContentPublished");

        Assert.Equivalent(
            new UnknownValues(
                ["ContentPublished"],
                ["Umbraco.ContentPublish", "Umbraco.ContentUnpublish", "Umbraco.MediaSave"]
            ),
            result.UnknownValues,
            strict: true
        );
    }

    [Fact]
    public async Task CreateWebhookAsync_UnknownAlias_MessageReadsWithoutTheCommandLayer()
    {
        // #278: no suggestion and no CLI command text from the client, but still a full sentence.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "ContentPublished");

        Assert.Equal(
            "Unknown webhook event 'ContentPublished'. Umbraco would save the webhook but never "
                + "fire it.",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task CreateWebhookAsync_UnknownAlias_IsAnInvalidArgumentWithNoStatus()
    {
        // #280: nothing reached Umbraco, so this is the caller's input, not a server rejection.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "ContentPublished");

        Assert.Equal((FailureCategory.InvalidArgument, 0), (result.Category, result.StatusCode));
    }

    [Fact]
    public async Task CreateWebhookAsync_EventListUnreadable_KeepsTheRejectedCategory()
    {
        // The input may be fine; the check itself failed, so it is not an invalid argument.
        var handler = Wire.Routed(("/webhook/events", """{ "total": 0, "items": [] }"""));

        var result = await Create(handler, "Umbraco.ContentPublish");

        Assert.Equal(FailureCategory.RequestRejected, result.Category);
    }

    [Fact]
    public async Task CreateWebhookAsync_AliasInTheWrongCase_IsRefused()
    {
        // Matching is exact: Umbraco compares aliases as typed, so a case slip would never fire.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "umbraco.contentpublish");

        Assert.Equal(["umbraco.contentpublish"], result.UnknownValues?.Unknown);
    }

    [Fact]
    public async Task CreateWebhookAsync_EventListForbidden_IsRefusedWithoutPosting()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/webhook/events"),
                HttpStatusCode.Forbidden,
                ""
            )
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Create(handler, "Umbraco.ContentPublish");

        Assert.Contains("Could not read this instance's webhook events", result.ErrorMessage);
        handler.AssertNoRequest(HttpMethod.Post, "/webhook");
    }

    [Fact]
    public async Task CreateWebhookAsync_AliasOnALaterPage_IsAccepted()
    {
        // More events than one page holds (packages register their own): the guard reads on
        // rather than refusing an alias it never saw.
        var handler = new RoutingHandler()
            .When(
                r =>
                    r.RequestUri!.AbsolutePath.EndsWith("/webhook/events")
                    && r.RequestUri.Query.Contains("skip=0"),
                HttpStatusCode.OK,
                """{ "total": 1001, "items": [ { "alias": "Umbraco.ContentPublish" } ] }"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/webhook/events"),
                HttpStatusCode.OK,
                """{ "total": 1001, "items": [ { "alias": "Package.OrderPlaced" } ] }"""
            )
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Create(handler, "Package.OrderPlaced");

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateWebhookAsync_BlankAlias_IsRefusedWithoutPosting()
    {
        // #277: the shared guard rejects a blank value before reading the event list.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "");

        Assert.Equal("A webhook event alias cannot be empty.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateWebhookAsync_EmptyEventList_IsRefused()
    {
        var handler = Wire.Routed(("/webhook/events", """{ "total": 0, "items": [] }"""));

        var result = await Create(handler, "Umbraco.ContentPublish");

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_MapsAliasNameAndType()
    {
        var handler = Wire.Returning(Events);

        var result = await Wire.Client(handler)
            .GetWebhookEventsAsync(0, 100, CancellationToken.None);

        Assert.Equal(
            new WebhookEvent
            {
                Alias = "Umbraco.MediaSave",
                EventName = "Media Saved",
                EventType = "Media",
            },
            result.Data!.Items.Last()
        );
    }
}
