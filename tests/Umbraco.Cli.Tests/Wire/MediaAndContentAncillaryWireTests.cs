using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the media, dictionary and webhook write paths put on the wire (#187 Phase 2).
/// <para>
/// Media upload is the only two-step write in the client - it stages the bytes as a temporary
/// file, then creates the media item referencing it - so both requests are asserted. The
/// dictionary assertions cover #181: the CLI echoed unrecognised language codes back as saved,
/// and while the fix for that is Phase 4, the translations actually reaching the wire is the part
/// that can be pinned now.
/// </para>
/// </summary>
public class MediaAndContentAncillaryWireTests
{
    /// <summary>Answers everything with 200 and an empty body.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Blank() =>
        new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");

    // ── media ─────────────────────────────────────────────────────────────────

    /// <summary>Uploads a small file, passing the media type by id so no lookup is needed.</summary>
    /// <param name="handler">The handler to run against.</param>
    /// <returns>The task.</returns>
    private static async Task UploadAsync(RoutingHandler handler)
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("not really a jpeg"));
        await Wire.Client(handler)
            .UploadMediaAsync(
                null,
                "Blog Image",
                bytes,
                "blog-1.jpg",
                "image/jpeg",
                Guid.NewGuid().ToString(),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task UploadMediaAsync_StagesTheFileThenCreatesTheMediaItem()
    {
        var handler = Blank();

        await UploadAsync(handler);

        // Two requests, in order: stage the bytes, then create the item pointing at them.
        // Collapsing them into one would create a media item with no file.
        handler.UriOf(HttpMethod.Post, "/temporary-file");
        var body = handler.BodyOf(HttpMethod.Post, "/media");
        Assert.Equal(
            "Blog Image",
            Assert.Single(body["variants"]!.AsArray())!["name"]!.GetValue<string>()
        );
        Assert.Equal(
            "umbracoFile",
            Assert.Single(body["values"]!.AsArray())!["alias"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UploadMediaAsync_LinksTheCreateToTheStagedTemporaryFile()
    {
        var handler = Blank();

        await UploadAsync(handler);

        // The id staged in the multipart body must be the one the create references, or the item
        // is created pointing at nothing.
        var staged = handler.RawBodyOf(HttpMethod.Post, "/temporary-file");
        var referenced = handler
            .BodyOf(HttpMethod.Post, "/media")["values"]![0]!["value"]!["temporaryFileId"]!
            .GetValue<string>();

        Assert.Contains(referenced, staged);
    }

    [Fact]
    public async Task MoveMediaAsync_NoParent_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).MoveMediaAsync(id, null, CancellationToken.None);

        Assert.False(handler.BodyOf(HttpMethod.Put, $"/media/{id}/move").ContainsKey("target"));
    }

    [Fact]
    public async Task RestoreMediaAsync_WithParent_SendsTheTarget()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).RestoreMediaAsync(id, parent, CancellationToken.None);

        Assert.Equal(
            parent.ToString(),
            handler.BodyOf(HttpMethod.Put, $"/recycle-bin/media/{id}/restore")["target"]!["id"]!
                .GetValue<string>()
        );
    }

    [Fact]
    public async Task TrashMediaAsync_PutsToMoveToRecycleBin()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).TrashMediaAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Put, $"/media/{id}/move-to-recycle-bin");
    }

    [Fact]
    public async Task DeleteMediaAsync_DeletesTheItem()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).DeleteMediaAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, $"/media/{id}");
    }

    [Fact]
    public async Task EmptyMediaRecycleBinAsync_DeletesTheRecycleBinNotTheMediaTree()
    {
        var handler = Blank();

        await Wire.Client(handler).EmptyMediaRecycleBinAsync(CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, "/recycle-bin/media");
    }

    // ── dictionary ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDictionaryItemAsync_SendsEveryTranslation()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "en-US", Translation = "Read more" },
                        new DictionaryTranslation { IsoCode = "da-DK", Translation = "Laes mere" },
                    ],
                },
                CancellationToken.None
            );

        // #181: the CLI reports these as saved even when Umbraco discards an unknown iso code.
        // What can be pinned here is that both actually leave the client.
        var translations = handler
            .BodyOf(HttpMethod.Post, "/dictionary")["translations"]!
            .AsArray();
        Assert.Equal(
            ["en-US", "da-DK"],
            translations.Select(t => t!["isoCode"]!.GetValue<string>())
        );
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_NoParent_OmitsIt()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest { Name = "Blog.ReadMore" },
                CancellationToken.None
            );

        Assert.False(handler.BodyOf(HttpMethod.Post, "/dictionary").ContainsKey("parent"));
    }

    [Fact]
    public async Task MoveDictionaryItemAsync_NoTarget_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).MoveDictionaryItemAsync(id, null, CancellationToken.None);

        Assert.False(handler.BodyOf(HttpMethod.Put, $"/dictionary/{id}/move").ContainsKey("target"));
    }

    [Fact]
    public async Task DeleteDictionaryItemAsync_DeletesTheItem()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).DeleteDictionaryItemAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, $"/dictionary/{id}");
    }

    // ── webhooks ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWebhookAsync_SendsUrlEventsAndHeaders()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateWebhookAsync(
                new CreateWebhookRequest
                {
                    Url = "https://example.com/hook",
                    Name = "Publish hook",
                    Events = ["Umbraco.ContentPublish"],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/webhook");
        Assert.Equal("https://example.com/hook", body["url"]!.GetValue<string>());
        Assert.Equal(
            "Umbraco.ContentPublish",
            Assert.Single(body["events"]!.AsArray())!.GetValue<string>()
        );
    }

    [Fact]
    public async Task CreateWebhookAsync_NoEvents_SendsAnEmptyArrayNotAMissingKey()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateWebhookAsync(
                new CreateWebhookRequest { Url = "https://example.com/hook" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/webhook");
        Assert.True(body.ContainsKey("events"));
        Assert.Empty(body["events"]!.AsArray());
    }

    [Fact]
    public async Task DeleteWebhookAsync_DeletesTheWebhook()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).DeleteWebhookAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, $"/webhook/{id}");
    }
}
