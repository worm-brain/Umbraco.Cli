using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the by-id reads actually surface (#187 Phase 3).
/// <para>
/// Each of these methods was fetching a rich body from the Management API and copying a handful of
/// fields out of a hand-written record, so the rest was dropped on the floor: a document's property
/// values (#168), a media item's dimensions (#172), a document type's properties (#160), a data
/// type's configuration (#170), and every type reference's alias (#163). The tests assert the
/// fields survive the mapping, which is the step that lost them.
/// </para>
/// </summary>
public class WidenedReadTests
{
    /// <summary>Routes each GET path fragment to its own canned body.</summary>
    /// <param name="routes">Path fragment to response body.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Routed(params (string Fragment, string Json)[] routes)
    {
        var handler = new RoutingHandler();
        foreach (var (fragment, json) in routes)
            handler.When(
                r =>
                    r.RequestUri!.AbsoluteUri.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase
                    ),
                HttpStatusCode.OK,
                json
            );
        return handler.When(_ => true, HttpStatusCode.OK, "");
    }

    // ── #168 + #163: content ──────────────────────────────────────────────────

    [Fact]
    public async Task GetContentByIdAsync_ReturnsValuesVariantsAndTemplate()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var typeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var templateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var handler = Routed(
            (
                $"document/{id}",
                $$"""
                {
                  "id": "{{id}}",
                  "documentType": { "id": "{{typeId}}" },
                  "template": { "id": "{{templateId}}" },
                  "values": [
                    { "alias": "title", "culture": "en-US", "segment": null, "editorAlias": "Umbraco.TextBox", "value": "Hello" },
                    { "alias": "count", "culture": null, "segment": null, "editorAlias": "Umbraco.Integer", "value": 5 }
                  ],
                  "variants": [
                    { "culture": "en-US", "segment": null, "name": "Hello", "state": "Published" },
                    { "culture": "da-DK", "segment": null, "name": "Hej", "state": "Draft" }
                  ]
                }
                """
            ),
            ($"document-type/{typeId}", $$"""{ "id": "{{typeId}}", "alias": "blogPost" }""")
        );

        var result = await Wire.Client(handler).GetContentByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var data = result.Data!;
        Assert.Equal(templateId, data.Template!.Id);
        Assert.Equal(2, data.Values!.Count());
        Assert.Equal(2, data.Variants!.Count());
        // The editor alias is what tells a caller the value's shape (#174).
        Assert.Equal("Umbraco.TextBox", data.Values!.First().EditorAlias);
        Assert.Equal("Hello", data.Values!.First().Value!.GetValue<string>());
        // Numbers must survive as numbers, not as strings.
        Assert.Equal(5, data.Values!.Last().Value!.GetValue<int>());
        // Every culture, and each culture's own state - not just the first variant.
        Assert.Equal("da-DK", data.Variants!.Last().Culture);
        Assert.Equal("Draft", data.Variants!.Last().State);
    }

    [Fact]
    public async Task GetContentByIdAsync_ResolvesTheDocumentTypeAlias()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var typeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = Routed(
            (
                $"document/{id}",
                $$"""{ "id": "{{id}}", "documentType": { "id": "{{typeId}}" }, "variants": [] }"""
            ),
            ($"document-type/{typeId}", $$"""{ "id": "{{typeId}}", "alias": "blogPost" }""")
        );

        var result = await Wire.Client(handler).GetContentByIdAsync(id, CancellationToken.None);

        // #163: the document body carries only the type's id, so the alias has to be looked up.
        // It used to be left as "", which reads as "this type has no alias".
        Assert.Equal("blogPost", result.Data!.ContentType!.Alias);
    }

    [Fact]
    public async Task GetContentByIdAsync_UnreadableDocumentType_LeavesTheAliasNullNotEmpty()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var typeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsoluteUri.Contains("document-type"),
                HttpStatusCode.NotFound,
                ""
            )
            .When(
                _ => true,
                HttpStatusCode.OK,
                $$"""{ "id": "{{id}}", "documentType": { "id": "{{typeId}}" }, "variants": [] }"""
            );

        var result = await Wire.Client(handler).GetContentByIdAsync(id, CancellationToken.None);

        // A type that cannot be read must not fail the document read, and must not fake an alias:
        // null is omitted from the envelope, "" would look like a real empty alias.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.ContentType!.Alias);
    }

    // ── #160: document types ──────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentTypeByIdAsync_ReturnsPropertiesContainersAndTemplates()
    {
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var dataTypeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var templateId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var containerId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var handler = Routed(
            (
                "document-type",
                $$"""
                {
                  "id": "{{id}}",
                  "name": "Blog Post",
                  "alias": "blogPost",
                  "icon": "icon-article",
                  "variesByCulture": true,
                  "properties": [
                    {
                      "id": "88888888-8888-8888-8888-888888888888",
                      "alias": "title",
                      "name": "Title",
                      "dataType": { "id": "{{dataTypeId}}" },
                      "container": { "id": "{{containerId}}" },
                      "sortOrder": 1,
                      "variesByCulture": true
                    }
                  ],
                  "containers": [
                    { "id": "{{containerId}}", "name": "Content", "type": "Group", "sortOrder": 0 }
                  ],
                  "allowedTemplates": [ { "id": "{{templateId}}" } ],
                  "defaultTemplate": { "id": "{{templateId}}" }
                }
                """
            )
        );

        var result = await Wire.Client(handler)
            .GetDocumentTypeByIdAsync(id, CancellationToken.None);

        var data = result.Data!;
        // The help text claimed these were returned for three releases while they were dropped.
        var property = Assert.Single(data.Properties!);
        Assert.Equal("title", property.Alias);
        Assert.Equal(dataTypeId, property.DataType);
        Assert.Equal(containerId, property.Container);
        Assert.True(property.VariesByCulture);
        Assert.Equal("Content", Assert.Single(data.Containers!).Name);
        Assert.Equal(templateId, Assert.Single(data.AllowedTemplates!));
        Assert.Equal(templateId, data.DefaultTemplate);
        Assert.True(data.VariesByCulture);
        Assert.Equal("icon-article", data.Icon);
    }

    // ── #170: data types ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetDataTypeByIdAsync_ReturnsTheEditorConfiguration()
    {
        var id = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var handler = Routed(
            (
                "data-type",
                $$"""
                {
                  "id": "{{id}}",
                  "name": "Blog Categories",
                  "editorAlias": "Umbraco.DropDown.Flexible",
                  "editorUiAlias": "Umb.PropertyEditorUi.Dropdown",
                  "values": [
                    { "alias": "items", "value": ["News", "Opinion"] },
                    { "alias": "multiple", "value": true }
                  ]
                }
                """
            )
        );

        var result = await Wire.Client(handler).GetDataTypeByIdAsync(id, CancellationToken.None);

        // #170: values holds the dropdown's items. Without it there is no way to see what a data
        // type is configured to offer.
        var values = result.Data!.Values!.ToList();
        Assert.Equal(2, values.Count);
        Assert.Equal(
            ["News", "Opinion"],
            values[0].Value!.AsArray().Select(v => v!.GetValue<string>())
        );
        Assert.True(values[1].Value!.GetValue<bool>());
    }

    // ── #172: media ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMediaByIdAsync_ReturnsFileMetadataAndUrl()
    {
        var id = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var typeId = Guid.Parse("13131313-1313-1313-1313-131313131313");
        var handler = Routed(
            (
                "media/urls",
                $$"""
                [ { "id": "{{id}}", "urlInfos": [ { "culture": null, "url": "/media/abc/blog-1.jpg" } ] } ]
                """
            ),
            (
                $"media/{id}",
                $$"""
                {
                  "id": "{{id}}",
                  "mediaType": { "id": "{{typeId}}" },
                  "values": [
                    { "alias": "umbracoWidth", "editorAlias": "Umbraco.Label", "value": 1200 },
                    { "alias": "umbracoBytes", "editorAlias": "Umbraco.Label", "value": 84213 },
                    { "alias": "umbracoExtension", "editorAlias": "Umbraco.Label", "value": "jpg" }
                  ],
                  "variants": [ { "culture": null, "name": "Blog Image" } ]
                }
                """
            ),
            (
                $"media-type/{typeId}",
                $$"""{ "id": "{{typeId}}", "name": "Hero Banner", "alias": "heroBanner" }"""
            )
        );

        var result = await Wire.Client(handler).GetMediaByIdAsync(id, CancellationToken.None);

        var data = result.Data!;
        // All three were already in the body being fetched and thrown away (#172).
        Assert.Equal(3, data.Values!.Count());
        Assert.Equal(1200, data.Values!.First().Value!.GetValue<int>());
        // The URL is not on the by-id body at all - it needs the separate urls endpoint.
        Assert.Equal("/media/abc/blog-1.jpg", Assert.Single(data.Urls!).Url);
        // The NAME, deliberately, even though the field is called alias: `media upload
        // --media-type` resolves media types by name, so returning the alias here would hand back
        // a value the write side cannot accept whenever the two differ - as they do here.
        Assert.Equal("Hero Banner", data.MediaType!.Alias);
    }

    [Fact]
    public async Task GetMediaByIdAsync_UnreachableUrlsEndpoint_StillReturnsTheItem()
    {
        var id = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var handler = new RoutingHandler()
            .When(r => r.RequestUri!.AbsoluteUri.Contains("urls"), HttpStatusCode.NotFound, "")
            .When(
                _ => true,
                HttpStatusCode.OK,
                $$"""{ "id": "{{id}}", "values": [], "variants": [ { "name": "Blog Image" } ] }"""
            );

        var result = await Wire.Client(handler).GetMediaByIdAsync(id, CancellationToken.None);

        // The URL is a second call, so it must degrade rather than fail the read it decorates.
        Assert.True(result.IsSuccess);
        Assert.Equal("Blog Image", result.Data!.Name);
        Assert.Null(result.Data.Urls);
    }

    // ── the value converter's edges ───────────────────────────────────────────

    [Fact]
    public async Task GetContentByIdAsync_NullAndNestedValues_SurviveTheConversion()
    {
        var id = Guid.Parse("14141414-1414-1414-1414-141414141414");
        var handler = Routed(
            (
                $"document/{id}",
                $$"""
                {
                  "id": "{{id}}",
                  "values": [
                    { "alias": "empty", "value": null },
                    { "alias": "rich", "value": { "markup": "<p>hi</p>", "blocks": null } },
                    { "alias": "tags", "value": ["a", "b"] }
                  ],
                  "variants": []
                }
                """
            )
        );

        var values = (
            await Wire.Client(handler).GetContentByIdAsync(id, CancellationToken.None)
        ).Data!.Values!.ToList();

        // A null property value must come back as JSON null, not as the name of a Kiota type -
        // no Untyped* type overrides ToString(), so stringifying one would put
        // "Microsoft.Kiota...UntypedString" in the payload as if it were the value.
        Assert.Null(values[0].Value);
        Assert.Equal("<p>hi</p>", values[1].Value!["markup"]!.GetValue<string>());
        Assert.Null(values[1].Value!["blocks"]);
        Assert.Equal(["a", "b"], values[2].Value!.AsArray().Select(v => v!.GetValue<string>()));
    }
}
