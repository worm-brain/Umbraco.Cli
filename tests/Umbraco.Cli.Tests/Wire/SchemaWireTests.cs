using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the schema write paths - document types, media types, data types, languages and
/// templates - put on the wire (#187 Phase 2).
/// <para>
/// Four of these are read-merge PUTs against replace-semantics endpoints, the shape that lost the
/// template on documents (#178). The data-type one matters most: it carries the editor
/// <c>values</c>, which are a dropdown's items or a picker's filters (#169/#170), and dropping
/// them would silently gut the data type.
/// </para>
/// </summary>
public class SchemaWireTests
{
    // ── document types ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentTypeAsync_SendsIdNameAliasAndFlags()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateDocumentTypeAsync(
                new CreateDocumentTypeRequest
                {
                    Id = id,
                    Name = "Blog Post",
                    Alias = "blogPost",
                    Icon = "icon-article",
                    IsElement = false,
                    AllowedAsRoot = true,
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/document-type");
        Assert.Equal(id.ToString(), body["id"]!.GetValue<string>());
        Assert.Equal("blogPost", body["alias"]!.GetValue<string>());
        Assert.Equal("icon-article", body["icon"]!.GetValue<string>());
        // false and true must both be present: omitted is not the same as false here.
        Assert.False(body["isElement"]!.GetValue<bool>());
        Assert.True(body["allowedAsRoot"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DeleteDocumentTypeAsync_DeletesTheType()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteDocumentTypeAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/document-type/{id}");
    }

    // ── media types ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMediaTypeAsync_SendsNameAliasAndFlags()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateMediaTypeAsync(
                new CreateMediaTypeRequest
                {
                    Name = "Brochure",
                    Alias = "brochure",
                    AllowedAsRoot = true,
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/media-type");
        Assert.Equal("brochure", body["alias"]!.GetValue<string>());
        Assert.True(body["allowedAsRoot"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DeleteMediaTypeAsync_DeletesTheType()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteMediaTypeAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/media-type/{id}");
    }

    // ── data types ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDataTypeAsync_SendsNameAndBothEditorAliases()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateDataTypeAsync(
                new CreateDataTypeRequest
                {
                    Name = "Blog Categories",
                    EditorAlias = "Umbraco.DropDown.Flexible",
                    EditorUiAlias = "Umb.PropertyEditorUi.Dropdown",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/data-type");
        Assert.Equal("Umbraco.DropDown.Flexible", body["editorAlias"]!.GetValue<string>());
        Assert.Equal("Umb.PropertyEditorUi.Dropdown", body["editorUiAlias"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateDataTypeAsync_NameOnly_KeepsTheEditorConfiguration()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Existing(
            $$"""
            {
              "id": "{{id}}",
              "name": "Blog Categories",
              "editorAlias": "Umbraco.DropDown.Flexible",
              "editorUiAlias": "Umb.PropertyEditorUi.Dropdown",
              "values": [ { "alias": "items", "value": ["News", "Opinion"] } ]
            }
            """
        );

        await Wire.Client(handler)
            .UpdateDataTypeAsync(
                id,
                new UpdateDataTypeRequest { Name = "Renamed" },
                CancellationToken.None
            );

        // values carries the dropdown's items (#169/#170). A replace-semantics PUT that omitted
        // them would silently empty the dropdown everywhere it is used.
        var body = handler.BodyOf(HttpMethod.Put, $"/data-type/{id}");
        Assert.Equal("Renamed", body["name"]!.GetValue<string>());
        Assert.Equal("Umbraco.DropDown.Flexible", body["editorAlias"]!.GetValue<string>());
        var items = Assert.Single(body["values"]!.AsArray());
        Assert.Equal(
            ["News", "Opinion"],
            items!["value"]!.AsArray().Select(v => v!.GetValue<string>())
        );
    }

    [Fact]
    public async Task CopyDataTypeAsync_NoTarget_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).CopyDataTypeAsync(id, null, CancellationToken.None);

        Assert.False(
            handler.BodyOf(HttpMethod.Post, $"/data-type/{id}/copy").ContainsKey("target")
        );
    }

    [Fact]
    public async Task MoveDataTypeAsync_WithTarget_SendsIt()
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).MoveDataTypeAsync(id, target, CancellationToken.None);

        Assert.Equal(
            target.ToString(),
            handler.BodyOf(HttpMethod.Put, $"/data-type/{id}/move")["target"]![
                "id"
            ]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task DeleteDataTypeAsync_DeletesTheType()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteDataTypeAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/data-type/{id}");
    }

    // ── languages ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateLanguageAsync_SendsIsoCodeNameAndFlags()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateLanguageAsync(
                new CreateLanguageRequest
                {
                    IsoCode = "da-DK",
                    Name = "Danish",
                    IsDefault = false,
                    IsMandatory = true,
                    FallbackIsoCode = "en-US",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/language");
        Assert.Equal("da-DK", body["isoCode"]!.GetValue<string>());
        Assert.True(body["isMandatory"]!.GetValue<bool>());
        Assert.Equal("en-US", body["fallbackIsoCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateLanguageAsync_NoFallback_OmitsIt()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateLanguageAsync(
                new CreateLanguageRequest { IsoCode = "da-DK", Name = "Danish" },
                CancellationToken.None
            );

        Assert.Null(handler.BodyOf(HttpMethod.Post, "/language")["fallbackIsoCode"]);
    }

    [Fact]
    public async Task UpdateLanguageAsync_NameOnly_KeepsTheFallbackAndFlags()
    {
        var handler = Wire.Existing(
            """
            {
              "isoCode": "da-DK",
              "name": "Danish",
              "isDefault": false,
              "isMandatory": true,
              "fallbackIsoCode": "en-US"
            }
            """
        );

        await Wire.Client(handler)
            .UpdateLanguageAsync(
                "da-DK",
                new UpdateLanguageRequest { Name = "Dansk" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, "/language/da-DK");
        Assert.Equal("Dansk", body["name"]!.GetValue<string>());
        Assert.Equal("en-US", body["fallbackIsoCode"]!.GetValue<string>());
        Assert.True(body["isMandatory"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DeleteLanguageAsync_UsesTheIsoCodeInThePath()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteLanguageAsync("da-DK", CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, "/language/da-DK");
    }

    // ── templates ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTemplateAsync_SendsNameAliasAndContent()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateTemplateAsync(
                new CreateTemplateRequest
                {
                    Name = "Blog Post",
                    Alias = "blogPost",
                    Content = "@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/template");
        Assert.Equal("blogPost", body["alias"]!.GetValue<string>());
        Assert.Contains("UmbracoViewPage", body["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateTemplateAsync_ContentOnly_KeepsTheNameAndAlias()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Existing(
            $$"""
            {
              "id": "{{id}}",
              "name": "Blog Post",
              "alias": "blogPost",
              "content": "old razor"
            }
            """
        );

        await Wire.Client(handler)
            .UpdateTemplateAsync(
                id,
                new UpdateTemplateRequest { Content = "new razor" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/template/{id}");
        Assert.Equal("new razor", body["content"]!.GetValue<string>());
        // Renaming the alias here would break every document using the template.
        Assert.Equal("blogPost", body["alias"]!.GetValue<string>());
        Assert.Equal("Blog Post", body["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteTemplateAsync_DeletesTheTemplate()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteTemplateAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/template/{id}");
    }
}
