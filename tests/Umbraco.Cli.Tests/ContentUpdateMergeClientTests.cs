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
    /// <summary>Builds a client whose HTTP calls are answered by <paramref name="handler"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <returns>A client bound to the handler.</returns>
    private static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

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
            && r.RequestUri!.AbsolutePath.EndsWith(
                $"/document/{id}",
                StringComparison.OrdinalIgnoreCase
            )
        );

    [Fact]
    public async Task UpdateContentAsync_ValueNotInTheBody_IsKept()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, Guid.NewGuid());
        var client = Client(handler);

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
        var client = Client(handler);

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
        var client = Client(handler);

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
        var client = Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest { Variants = [new ContentVariant { Name = "Renamed" }] },
            ct: CancellationToken.None
        );

        Assert.Equal(
            templateId.ToString(),
            PutBody(handler, id)["template"]!["id"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UpdateContentAsync_Replace_DropsUnlistedValuesButStillKeepsTheTemplate()
    {
        var id = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var handler = Handler(id, templateId);
        var client = Client(handler);

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
            replace: true,
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
        var client = Client(handler);

        await client.UpdateContentAsync(
            id,
            new UpdateContentRequest { Variants = [new ContentVariant { Name = "Renamed" }] },
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
        var client = Client(handler);

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
