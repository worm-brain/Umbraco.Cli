using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The culture a create sends when the caller named none (#228). A flags-only create on a
/// document type that varies by culture used to send a null culture, which Umbraco rejects with
/// "The content type variance did not match that of the passed content data". It now sends the
/// instance's default language, and an invariant type still sends no culture.
/// </summary>
public class CreateCultureDefaultWireTests
{
    private const string Languages = """
        { "total": 2, "items": [
          { "isoCode": "da-DK", "isDefault": false },
          { "isoCode": "en-US", "isDefault": true }
        ] }
        """;

    /// <summary>A handler whose document type does (or does not) vary by culture.</summary>
    /// <param name="variesByCulture">What the document type GET reports.</param>
    /// <param name="languages">The language list body.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler TypeHandler(bool variesByCulture, string languages = Languages) =>
        Wire.Routed(
            (
                "/document-type/",
                $$"""{ "variesByCulture": {{(variesByCulture ? "true" : "false")}} }"""
            ),
            ("/language", languages)
        );

    /// <summary>A content create request with one culture-less (or explicit) variant.</summary>
    /// <param name="culture">The variant culture, or null.</param>
    /// <returns>The request.</returns>
    private static CreateContentRequest ContentRequest(string? culture = null) =>
        new()
        {
            ContentType = new ContentTypeReference { Id = Guid.NewGuid() },
            Variants = [new ContentVariant { Name = "Post", Culture = culture }],
        };

    /// <summary>The culture of the first variant of the captured POST body.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <param name="path">The POST path suffix.</param>
    /// <returns>The culture node (null when JSON null or absent).</returns>
    private static JsonNode? PostedCulture(RoutingHandler handler, string path) =>
        handler.BodyOf(HttpMethod.Post, path)["variants"]![0]!["culture"];

    [Fact]
    public async Task CreateContentAsync_VariantTypeWithNoCulture_SendsTheDefaultLanguage()
    {
        var handler = TypeHandler(variesByCulture: true);

        var result = await Wire.Client(handler)
            .CreateContentAsync(ContentRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("en-US", PostedCulture(handler, "/document")!.GetValue<string>());
    }

    [Fact]
    public async Task CreateContentAsync_TwoCreatesOnOneClient_ReadTheLanguagesOnce()
    {
        var handler = TypeHandler(variesByCulture: true);
        var client = Wire.Client(handler);

        await client.CreateContentAsync(ContentRequest(), CancellationToken.None);
        await client.CreateContentAsync(ContentRequest(), CancellationToken.None);

        Assert.Single(
            handler.Recordings,
            r => r.Uri.AbsolutePath.EndsWith("/language", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task CreateContentAsync_InvariantTypeWithNoCulture_SendsANullCulture()
    {
        var handler = TypeHandler(variesByCulture: false);

        await Wire.Client(handler).CreateContentAsync(ContentRequest(), CancellationToken.None);

        Assert.Null(PostedCulture(handler, "/document"));
    }

    [Fact]
    public async Task CreateContentAsync_ExplicitCulture_KeepsItWithoutReadingTheType()
    {
        var handler = TypeHandler(variesByCulture: true);

        await Wire.Client(handler)
            .CreateContentAsync(ContentRequest("da-DK"), CancellationToken.None);

        Assert.Equal("da-DK", PostedCulture(handler, "/document")!.GetValue<string>());
        Assert.DoesNotContain(
            handler.Recordings,
            r => r.Uri.AbsolutePath.Contains("/document-type/", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task CreateContentAsync_VariantTypeAndNoDefaultLanguage_FailsWithoutCreating()
    {
        var handler = TypeHandler(
            variesByCulture: true,
            """{ "total": 1, "items": [ { "isoCode": "da-DK", "isDefault": false } ] }"""
        );

        var result = await Wire.Client(handler)
            .CreateContentAsync(ContentRequest(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("--culture", result.ErrorMessage);
        handler.AssertNoRequest(HttpMethod.Post, "/document");
    }

    [Fact]
    public async Task CreateDocumentBlueprintAsync_VariantTypeWithNoCulture_SendsTheDefaultLanguage()
    {
        var handler = TypeHandler(variesByCulture: true);

        await Wire.Client(handler)
            .CreateDocumentBlueprintAsync(
                new CreateDocumentBlueprintRequest
                {
                    DocumentType = new ContentTypeReference { Id = Guid.NewGuid() },
                    Variants = [new ContentVariant { Name = "Starter" }],
                },
                CancellationToken.None
            );

        Assert.Equal("en-US", PostedCulture(handler, "/document-blueprint")!.GetValue<string>());
    }

    // ── blueprint update --name (the existing-item twin) ──────────────────────

    /// <summary>A blueprint handler whose GET returns variants in the given cultures.</summary>
    /// <param name="cultures">The blueprint's variant cultures.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler BlueprintHandler(params string[] cultures)
    {
        var variants = string.Join(
            ",",
            cultures.Select(c =>
                $$"""{ "culture": "{{c}}", "segment": null, "name": "Old {{c}}" }"""
            )
        );
        return new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/language"),
                HttpStatusCode.OK,
                Languages
            )
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{ "values": [], "variants": [{{variants}}] }"""
            )
            .When(_ => true, HttpStatusCode.OK, "");
    }

    [Fact]
    public async Task UpdateDocumentBlueprintAsync_VariantBlueprintRenamedWithNoCulture_RenamesTheDefaultLanguage()
    {
        var id = Guid.NewGuid();
        var handler = BlueprintHandler("en-US", "da-DK");

        var result = await Wire.Client(handler)
            .UpdateDocumentBlueprintAsync(
                id,
                new UpdateDocumentBlueprintRequest
                {
                    Variants = [new ContentVariant { Name = "New" }],
                },
                ct: CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var names = handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{id}")["variants"]!
            .AsArray()
            .ToDictionary(
                v => v!["culture"]!.GetValue<string>(),
                v => v!["name"]!.GetValue<string>()
            );
        Assert.Equal(
            new Dictionary<string, string> { ["en-US"] = "New", ["da-DK"] = "Old da-DK" },
            names
        );
    }

    [Fact]
    public async Task UpdateDocumentBlueprintAsync_VariantBlueprintWithoutTheDefaultLanguage_IsRefused()
    {
        var id = Guid.NewGuid();
        var handler = BlueprintHandler("da-DK");

        var result = await Wire.Client(handler)
            .UpdateDocumentBlueprintAsync(
                id,
                new UpdateDocumentBlueprintRequest
                {
                    Variants = [new ContentVariant { Name = "New" }],
                },
                ct: CancellationToken.None
            );

        Assert.Equal(400, result.StatusCode);
        handler.AssertNoRequest(HttpMethod.Put, $"/document-blueprint/{id}");
    }

    // content update gets the same culture default as blueprint update (#264).

    /// <summary>A document handler whose GET returns variants in the given cultures (null = invariant).</summary>
    /// <param name="cultures">The document's variant cultures.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler DocumentHandler(params string?[] cultures)
    {
        var variants = string.Join(
            ",",
            cultures.Select(c =>
                c is null
                    ? """{ "culture": null, "segment": null, "name": "Old" }"""
                    : $$"""{ "culture": "{{c}}", "segment": null, "name": "Old {{c}}" }"""
            )
        );
        return new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/language"),
                HttpStatusCode.OK,
                Languages
            )
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{ "values": [], "variants": [{{variants}}] }"""
            )
            .When(_ => true, HttpStatusCode.OK, "");
    }

    /// <summary>The variant names in the document PUT, keyed by culture ("" for invariant).</summary>
    private static Dictionary<string, string> PutNames(RoutingHandler handler, Guid id) =>
        handler.BodyOf(HttpMethod.Put, $"/document/{id}")["variants"]!
            .AsArray()
            .ToDictionary(
                v => v!["culture"]?.GetValue<string>() ?? "",
                v => v!["name"]!.GetValue<string>()
            );

    [Fact]
    public async Task UpdateContentAsync_VariantDocumentRenamedWithNoCulture_RenamesTheDefaultLanguage()
    {
        var id = Guid.NewGuid();
        var handler = DocumentHandler("en-US", "da-DK");

        await Wire.Client(handler)
            .UpdateContentAsync(
                id,
                new UpdateContentRequest { Variants = [new ContentVariant { Name = "New" }] },
                ct: CancellationToken.None
            );

        Assert.Equal(
            new Dictionary<string, string> { ["en-US"] = "New", ["da-DK"] = "Old da-DK" },
            PutNames(handler, id)
        );
    }

    [Fact]
    public async Task UpdateContentAsync_InvariantDocumentRenamedWithNoCulture_StaysInvariant()
    {
        var id = Guid.NewGuid();
        var handler = DocumentHandler((string?)null);

        await Wire.Client(handler)
            .UpdateContentAsync(
                id,
                new UpdateContentRequest { Variants = [new ContentVariant { Name = "New" }] },
                ct: CancellationToken.None
            );

        Assert.Equal(new Dictionary<string, string> { [""] = "New" }, PutNames(handler, id));
    }
}
