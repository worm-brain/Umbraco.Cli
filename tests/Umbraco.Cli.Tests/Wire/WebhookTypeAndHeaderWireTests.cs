using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The webhook <c>--type</c> checks (#368) and header removal (#367) on the wire: an unknown alias
/// carries the known ones for a suggestion, a filter the events can never match is refused, and an
/// empty header value drops that header from the <c>PUT</c>.
/// </summary>
public class WebhookTypeAndHeaderWireTests
{
    private static readonly Guid HookId = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");
    private static readonly Guid BlogPost = Guid.Parse("5a5a5a5a-1234-5678-abcd-ef0123456789");
    private static readonly Guid Image = Guid.Parse("7c7c7c7c-1234-5678-abcd-ef0123456789");

    /// <summary>
    /// A site with a document type <c>blogPost</c>, a media type <c>image</c> and no member types,
    /// plus one content event and one media event.
    /// </summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Site() =>
        Wire.Routed(
            (
                "webhook/events",
                """
                {"total":2,"items":[
                  {"eventName":"Content Published","eventType":"Content","alias":"Umbraco.ContentPublish"},
                  {"eventName":"Media Saved","eventType":"Media","alias":"Umbraco.MediaSave"}
                ]}
                """
            ),
            (
                "tree/document-type/root",
                $$"""{"total":1,"items":[{"id":"{{BlogPost}}","name":"Blog Post","isFolder":false}]}"""
            ),
            ($"document-type/{BlogPost}", $$"""{"id":"{{BlogPost}}","alias":"blogPost"}"""),
            (
                "tree/media-type/root",
                $$"""{"total":1,"items":[{"id":"{{Image}}","name":"Image","isFolder":false}]}"""
            ),
            ($"media-type/{Image}", $$"""{"id":"{{Image}}","alias":"image","name":"Image"}"""),
            ("tree/member-type/root", """{"total":0,"items":[]}""")
        );

    [Fact]
    public async Task ResolveWebhookTypesAsync_UnknownAlias_CarriesTheKnownAliases()
    {
        var result = await Wire.Client(Site())
            .ResolveWebhookTypesAsync(["blogpst"], null, CancellationToken.None);

        Assert.Equal(["blogPost", "image"], result.UnknownValues!.Known.Order());
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_DocumentTypeOnMediaOnlyEvents_IsRefused()
    {
        var result = await Wire.Client(Site())
            .ResolveWebhookTypesAsync(["blogPost"], ["Umbraco.MediaSave"], CancellationToken.None);

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_TypeMatchesOneOfTheEvents_IsAccepted()
    {
        var result = await Wire.Client(Site())
            .ResolveWebhookTypesAsync(
                ["image"],
                ["Umbraco.ContentPublish", "Umbraco.MediaSave"],
                CancellationToken.None
            );

        Assert.Equal([Image], result.Data!);
    }

    [Fact]
    public async Task ResolveWebhookTypesAsync_TypeGivenById_SkipsTheKindCheck()
    {
        // Its kind is not looked up, so the check cannot say it never matches.
        var result = await Wire.Client(Site())
            .ResolveWebhookTypesAsync(
                [BlogPost.ToString()],
                ["Umbraco.MediaSave"],
                CancellationToken.None
            );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateWebhookAsync_EmptyHeaderValue_RemovesThatHeader()
    {
        var handler = Wire.Existing(
            $$"""
            { "id": "{{HookId}}", "url": "https://my.app/hook", "events": [],
              "headers": { "X-Api-Key": "old", "X-Keep": "yes" } }
            """
        );

        await Wire.Client(handler)
            .UpdateWebhookAsync(
                HookId,
                new UpdateWebhookRequest
                {
                    Headers = new Dictionary<string, string> { ["x-api-key"] = "" },
                },
                CancellationToken.None
            );

        var headers = handler.BodyOf(HttpMethod.Put, $"/webhook/{HookId}")["headers"]!.AsObject();
        Assert.Equal(["X-Keep=yes"], headers.Select(h => $"{h.Key}={h.Value}"));
    }
}
