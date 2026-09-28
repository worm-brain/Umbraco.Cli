using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Wire-shape tests for the content update path (#178/#179). The Management API's
/// <c>PUT /document/{id}</c> is replace-semantics, so anything missing from the body is deleted.
/// These assert on the exact JSON that leaves the client - not on a fake's recorded arguments -
/// because the bug they cover was invisible to argument-level assertions for two releases.
/// </summary>
public class ContentUpdateMergeClientTests
{
    /// <summary>
    /// A document body in the shape the Management API returns: two values on one culture, a
    /// template, and an <c>editorAlias</c> on each value (present on the response model, absent
    /// from the request model).
    /// </summary>
    /// <param name="id">The document id to embed.</param>
    /// <param name="templateId">The template id to embed.</param>
    /// <returns>The JSON body.</returns>
    private static string ExistingDocument(Guid id, Guid templateId) =>
        $$"""
            {
              "id": "{{id}}",
              "template": { "id": "{{templateId}}" },
              "values": [
                { "alias": "title", "culture": "en-US", "segment": null, "value": "Hello", "editorAlias": "Umbraco.TextBox" },
                { "alias": "body", "culture": "en-US", "segment": null, "value": "Body text", "editorAlias": "Umbraco.RichText" }
              ],
              "variants": [
                { "culture": "en-US", "segment": null, "name": "Hello" }
              ]
            }
            """;

    /// <summary>Routes the document GET to an existing body and accepts any PUT.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="templateId">The template id on the existing document.</param>
    /// <returns>A configured handler.</returns>
    private static RoutingHandler Handler(Guid id, Guid templateId) =>
        new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                ExistingDocument(id, templateId)
            );

    /// <summary>Returns the captured PUT body for the document, parsed.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="id">The document id.</param>
    /// <returns>The parsed PUT body.</returns>
    private static JsonNode PutBody(RoutingHandler handler, Guid id) =>
        JsonNode.Parse(RawPutBody(handler, id))!;

    /// <summary>Returns the captured PUT body for the document, as raw text.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="id">The document id.</param>
    /// <returns>The raw PUT body.</returns>
    private static string RawPutBody(RoutingHandler handler, Guid id) =>
        handler.BodyForFirst(r =>
            r.Method == HttpMethod.Put
            && r.Uri.AbsolutePath.EndsWith($"/document/{id}", StringComparison.OrdinalIgnoreCase)
        );

    [Fact]
    public async Task UpdateContentAsync_ValueNotInTheBody_IsKept()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Wire.Client(handler);

        var result = await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Values =
                [
                    new ContentValue
                    {
                        Alias = "title",
                        Culture = "en-US",
                        Value = "Hej",
                    },
                ],
            },
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var values = PutBody(handler, id)["values"]!.AsArray();
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v!["value"]!.GetValue<string>() == "Body text");
    }

    [Fact]
    public async Task UpdateContentAsync_ValueInTheBody_ReplacesTheMatchingOneRatherThanDuplicating()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Values =
                [
                    new ContentValue
                    {
                        Alias = "title",
                        Culture = "en-US",
                        Value = "Hello again",
                    },
                ],
            },
            ct: CancellationToken.None
        );

        var titles = PutBody(handler, id)["values"]!
            .AsArray()
            .Where(v => v!["alias"]!.GetValue<string>() == "title")
            .ToList();
        Assert.Single(titles);
        Assert.Equal("Hello again", titles[0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateContentAsync_ValueForANewCulture_IsAppendedNotSwappedIn()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Values =
                [
                    new ContentValue
                    {
                        Alias = "title",
                        Culture = "da-DK",
                        Value = "Hej",
                    },
                ],
                Variants = [new ContentVariant { Culture = "da-DK", Name = "Hej" }],
            },
            ct: CancellationToken.None
        );

        var body = PutBody(handler, id);
        Assert.Equal(3, body["values"]!.AsArray().Count);
        Assert.Equal(2, body["variants"]!.AsArray().Count);
        Assert.Contains(
            body["values"]!.AsArray(),
            v =>
                v!["alias"]!.GetValue<string>() == "title"
                && v["culture"]!.GetValue<string>() == "en-US"
        );
    }

    [Fact]
    public async Task UpdateContentAsync_RequestWithoutATemplate_KeepsTheDocumentsTemplate()
    {
        var id = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var handler = Handler(id, templateId);
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Variants = [new ContentVariant { Culture = "en-US", Name = "Renamed" }],
            },
            ct: CancellationToken.None
        );

        var body = PutBody(handler, id);
        Assert.Equal(templateId.ToString(), body["template"]!["id"]!.GetValue<string>());
        // The rename must land on the existing variant, not alongside it.
        var variant = Assert.Single(body["variants"]!.AsArray());
        Assert.Equal("en-US", variant!["culture"]!.GetValue<string>());
        Assert.Equal("Renamed", variant["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateContentAsync_VariantWithNoCultureAndNoDefaultLanguageVariant_Fails()
    {
        // The document has only en-US, but the default language is da-DK (#264): there is no
        // default-language variant to rename, so the guard refuses and names the cultures.
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/language"),
                HttpStatusCode.OK,
                """{ "total": 1, "items": [ { "isoCode": "da-DK", "isDefault": true } ] }"""
            )
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                ExistingDocument(id, Guid.NewGuid())
            );
        var client = Wire.Client(handler);

        var result = await client.UpdateContentAsync(
            id,
            new UpdateContentRequest { Variants = [new ContentVariant { Name = "Renamed" }] },
            ct: CancellationToken.None
        );

        // Appending a null-culture variant beside the real ones would leave the rename undone and
        // produce a body Umbraco rejects, so this fails with a message naming the cultures.
        Assert.False(result.IsSuccess);
        Assert.Contains("varies by culture", result.ErrorMessage);
        Assert.Contains("en-US", result.ErrorMessage);
        Assert.DoesNotContain(handler.Requests, u => u.AbsoluteUri.Contains("PUT"));
    }

    [Fact]
    public async Task UpdateContentAsync_Replace_DropsUnlistedValuesButStillKeepsTheTemplate()
    {
        var id = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var handler = Handler(id, templateId);
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Values =
                [
                    new ContentValue
                    {
                        Alias = "title",
                        Culture = "en-US",
                        Value = "Only",
                    },
                ],
            },
            WriteMode.Replace,
            ct: CancellationToken.None
        );

        var body = PutBody(handler, id);
        Assert.Single(body["values"]!.AsArray());
        Assert.Equal(templateId.ToString(), body["template"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateContentAsync_CarriedOverValues_DoNotEchoEditorAlias()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Variants = [new ContentVariant { Culture = "en-US", Name = "Renamed" }],
            },
            ct: CancellationToken.None
        );

        Assert.DoesNotContain("editorAlias", RawPutBody(handler, id));
    }

    [Fact]
    public async Task UpdateContentAsync_TemplateById_OverwritesTheDocumentsTemplate()
    {
        var id = Guid.NewGuid();
        var newTemplate = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Wire.Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest
            {
                Template = new ContentTemplateReference { Id = newTemplate },
            },
            ct: CancellationToken.None
        );

        Assert.Equal(
            newTemplate.ToString(),
            PutBody(handler, id)["template"]!["id"]!.GetValue<string>()
        );
    }
}
