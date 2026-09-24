using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the document-blueprint, static-file, diagnostics, Examine and redirect write paths put on
/// the wire (#187 Phase 2).
/// <para>
/// Static files are the widest surface here: one set of verbs dispatches over three kinds, so a
/// mis-wired slug sends a stylesheet to the script endpoint. Redirect tracking is the #184 shape -
/// a POST with no body at all, whose entire effect is one query parameter.
/// </para>
/// </summary>
public class PlatformWireTests
{
    // ── document blueprints ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentBlueprintAsync_SendsDocumentTypeVariantsAndValues()
    {
        var typeId = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateDocumentBlueprintAsync(
                new CreateDocumentBlueprintRequest
                {
                    DocumentType = new ContentTypeReference { Id = typeId },
                    Variants = [new ContentVariant { Name = "Standard post" }],
                    Values = [new ContentValue { Alias = "title", Value = "Untitled" }],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/document-blueprint");
        Assert.Equal(typeId.ToString(), body["documentType"]!["id"]!.GetValue<string>());
        Assert.Equal(
            "Standard post",
            Assert.Single(body["variants"]!.AsArray())!["name"]!.GetValue<string>()
        );
        Assert.Equal(
            "title",
            Assert.Single(body["values"]!.AsArray())!["alias"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_SendsTheSourceDocument()
    {
        var document = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(
                new CreateBlueprintFromDocumentRequest { Document = document, Name = "From post" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/document-blueprint/from-document");
        Assert.Equal(document.ToString(), body["document"]!["id"]!.GetValue<string>());
        Assert.Equal("From post", body["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task MoveDocumentBlueprintAsync_NoTarget_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).MoveDocumentBlueprintAsync(id, null, CancellationToken.None);

        Assert.False(
            handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{id}/move").ContainsKey("target")
        );
    }

    [Fact]
    public async Task DeleteDocumentBlueprintAsync_DeletesTheBlueprint()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteDocumentBlueprintAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/document-blueprint/{id}");
    }

    // ── static files: one verb set, three kinds ───────────────────────────────

    [Theory]
    [InlineData(StaticFileKind.Script, "script")]
    [InlineData(StaticFileKind.Stylesheet, "stylesheet")]
    [InlineData(StaticFileKind.PartialView, "partial-view")]
    public async Task CreateStaticFileAsync_PostsToTheEndpointForItsKind(
        StaticFileKind kind,
        string slug
    )
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateStaticFileAsync(
                kind,
                new CreateStaticFileRequest { Name = "site.css", Content = "body{}" },
                CancellationToken.None
            );

        // A mis-wired slug would file a stylesheet under scripts, with a 200 to show for it.
        var body = handler.BodyOf(HttpMethod.Post, $"/{slug}");
        Assert.Equal("site.css", body["name"]!.GetValue<string>());
        Assert.Equal("body{}", body["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(StaticFileKind.Script, "script")]
    [InlineData(StaticFileKind.Stylesheet, "stylesheet")]
    [InlineData(StaticFileKind.PartialView, "partial-view")]
    public async Task UpdateStaticFileAsync_PutsTheContentForItsKind(
        StaticFileKind kind,
        string slug
    )
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .UpdateStaticFileAsync(
                kind,
                "site.css",
                new UpdateStaticFileRequest { Content = "body{color:red}" },
                CancellationToken.None
            );

        Assert.Equal(
            "body{color:red}",
            handler.BodyOf(HttpMethod.Put, $"/{slug}/site.css")["content"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task CreateStaticFileAsync_NoParentPath_OmitsIt()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateStaticFileAsync(
                StaticFileKind.Script,
                new CreateStaticFileRequest { Name = "app.js" },
                CancellationToken.None
            );

        Assert.Null(handler.BodyOf(HttpMethod.Post, "/script")["parent"]);
    }

    [Fact]
    public async Task DeleteStaticFileAsync_DeletesByPath()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .DeleteStaticFileAsync(StaticFileKind.Stylesheet, "site.css", CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, "/stylesheet/site.css");
    }

    // ── diagnostics, Examine, redirects ───────────────────────────────────────

    [Fact]
    public async Task CreateSavedLogSearchAsync_SendsNameAndQuery()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateSavedLogSearchAsync("Errors", "@Level='Error'", CancellationToken.None);

        var body = handler.BodyOf(HttpMethod.Post, "/saved-search");
        Assert.Equal("Errors", body["name"]!.GetValue<string>());
        Assert.Equal("@Level='Error'", body["query"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteSavedLogSearchAsync_DeletesByName()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteSavedLogSearchAsync("Errors", CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, "/saved-search/Errors");
    }

    [Fact]
    public async Task RebuildIndexAsync_PostsToTheNamedIndex()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).RebuildIndexAsync("ExternalIndex", CancellationToken.None);

        handler.AssertRequested(HttpMethod.Post, "/indexer/ExternalIndex/rebuild");
    }

    [Fact]
    public async Task BuildModelsAsync_PostsToTheBuildEndpoint()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).BuildModelsAsync(CancellationToken.None);

        handler.AssertRequested(HttpMethod.Post, "/models-builder/build");
    }

    [Theory]
    [InlineData(true, "Enabled")]
    [InlineData(false, "Disabled")]
    public async Task SetRedirectTrackingAsync_SendsTheStatusAsAQueryParameter(
        bool enabled,
        string expected
    )
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).SetRedirectTrackingAsync(enabled, CancellationToken.None);

        // #184's shape: no body at all, so the parameter name and value are the whole contract.
        Assert.Equal(
            expected,
            handler.QueryOf(HttpMethod.Post, "/redirect-management/status")["status"]
        );
    }
}
