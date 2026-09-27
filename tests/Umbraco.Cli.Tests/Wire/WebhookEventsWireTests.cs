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

        Assert.Equal(400, result.StatusCode);
        handler.AssertNoRequest(HttpMethod.Post, "/webhook");
    }

    [Fact]
    public async Task CreateWebhookAsync_UnknownAlias_SuggestsTheNearestAlias()
    {
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "ContentPublished");

        Assert.Contains("did you mean 'Umbraco.ContentPublish'", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateWebhookAsync_AliasInTheWrongCase_IsRefused()
    {
        // Matching is exact: Umbraco compares aliases as typed, so a case slip would never fire.
        var handler = Wire.Routed(("/webhook/events", Events));

        var result = await Create(handler, "umbraco.contentpublish");

        Assert.Contains("did you mean 'Umbraco.ContentPublish'", result.ErrorMessage);
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

    [Theory]
    [InlineData("ContentPublished", "Umbraco.ContentPublish")]
    [InlineData("Umbraco.ContentUnpublished", "Umbraco.ContentUnpublish")]
    [InlineData("mediasave", "Umbraco.MediaSave")]
    public void NearestWebhookEvent_CloseName_SuggestsTheAlias(string typed, string expected)
    {
        string[] known =
        [
            "Umbraco.ContentPublish",
            "Umbraco.ContentUnpublish",
            "Umbraco.MediaSave",
        ];

        Assert.Equal(expected, UmbracoManagementClient.NearestWebhookEvent(typed, known));
    }

    [Fact]
    public void NearestWebhookEvent_UnrelatedName_SuggestsNothing()
    {
        string[] known = ["Umbraco.ContentPublish", "Umbraco.MediaSave"];

        Assert.Null(UmbracoManagementClient.NearestWebhookEvent("MemberGroupDeleted", known));
    }
}
