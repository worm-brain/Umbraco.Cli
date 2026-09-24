using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The #187 Phase 4 fixes. Three of the four were silent wrong answers: a filter that matched
/// nothing, a create that reported translations as saved which Umbraco had discarded, and two
/// <c>get</c> commands that rejected the very key their documentation told you to use.
/// </summary>
public class CheapWinsTests
{
    /// <summary>Routes each path fragment to its own canned body.</summary>
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

    // ── #184: the group filter that matched nothing ───────────────────────────

    [Fact]
    public async Task GetMembersAsync_WithGroup_SendsMemberGroupNameNotFilter()
    {
        var handler = Wire.Returning("""{"total":0,"items":[]}""");

        await Wire.Client(handler).GetMembersAsync("Subscribers", 0, 20, CancellationToken.None);

        // `filter` is the free-text search over name and email, so a group name never matched and
        // the command returned an empty list with exit 0.
        var query = handler.QueryOf(HttpMethod.Get, "/filter/member");
        Assert.Equal("Subscribers", query["memberGroupName"]);
        Assert.Null(query["filter"]);
    }

    [Fact]
    public async Task GetMembersAsync_NoGroup_SendsNeitherParameter()
    {
        var handler = Wire.Returning("""{"total":0,"items":[]}""");

        await Wire.Client(handler).GetMembersAsync(null, 0, 20, CancellationToken.None);

        var query = handler.QueryOf(HttpMethod.Get, "/filter/member");
        Assert.Null(query["memberGroupName"]);
        Assert.Null(query["filter"]);
    }

    // ── #159: get by the key the docs promised ────────────────────────────────

    [Fact]
    public async Task GetDocumentTypeAsync_Guid_ReadsItDirectlyWithoutSearching()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = Routed(
            ($"document-type/{id}", $$"""{ "id": "{{id}}", "alias": "blogPost" }""")
        );

        var result = await Wire.Client(handler)
            .GetDocumentTypeAsync(id.ToString(), CancellationToken.None);

        Assert.Equal("blogPost", result.Data!.Alias);
        handler.AssertNoRequest(HttpMethod.Get, "/item/document-type/search");
    }

    [Fact]
    public async Task GetDataTypeAsync_Name_ResolvesItThenReads()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = Routed(
            (
                "item/data-type/search",
                $$"""{ "total": 1, "items": [ { "id": "{{id}}", "name": "Textstring" } ] }"""
            ),
            (
                $"data-type/{id}",
                $$"""{ "id": "{{id}}", "name": "Textstring", "editorAlias": "Umbraco.TextBox" }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetDataTypeAsync("Textstring", CancellationToken.None);

        // By NAME, not alias: a data type has no alias - editorAlias names the editor behind it,
        // which many data types share, so it does not identify one.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Umbraco.TextBox", result.Data!.EditorAlias);
    }

    [Fact]
    public async Task GetDataTypeAsync_UnknownName_FailsWithAUsableMessage()
    {
        var handler = Routed(("item/data-type/search", """{ "total": 0, "items": [] }"""));

        var result = await Wire.Client(handler)
            .GetDataTypeAsync("NotARealDataType", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Contains("NotARealDataType", result.ErrorMessage);
        // The message has to say what to do next, not just that it failed.
        Assert.Contains("data-types list", result.ErrorMessage);
    }

    // ── #181: translations reported as saved that Umbraco discarded ───────────

    [Fact]
    public async Task CreateDictionaryItemAsync_UnknownIsoCode_FailsInsteadOfReportingSuccess()
    {
        var handler = Routed(
            (
                "/language",
                """{ "total": 2, "items": [ { "isoCode": "en-US" }, { "isoCode": "da-DK" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "da", Translation = "Laes mere" },
                    ],
                },
                CancellationToken.None
            );

        // Umbraco accepts this and silently drops the translation, so the CLI used to report a
        // saved item that was actually empty.
        Assert.False(result.IsSuccess);
        Assert.Contains("'da'", result.ErrorMessage);
        Assert.Contains("en-US", result.ErrorMessage); // names the codes that would work
        handler.AssertNoRequest(HttpMethod.Post, "/dictionary");
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_KnownIsoCodes_Proceeds()
    {
        var handler = Routed(
            (
                "/language",
                """{ "total": 2, "items": [ { "isoCode": "en-US" }, { "isoCode": "da-DK" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "da-DK", Translation = "Laes mere" },
                    ],
                },
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        handler.AssertRequested(HttpMethod.Post, "/dictionary");
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_InstanceReportsNoLanguages_DoesNotBlockTheCreate()
    {
        var handler = Routed(("/language", """{ "total": 0, "items": [] }"""));

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "da-DK", Translation = "Laes mere" },
                    ],
                },
                CancellationToken.None
            );

        // An instance that reports no languages is not evidence the codes are wrong, so the guard
        // must not turn an unreadable language list into a blocked create.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        handler.AssertRequested(HttpMethod.Post, "/dictionary");
    }

    // ── #183: --fallback on create ────────────────────────────────────────────

    [Fact]
    public async Task CreateLanguageAsync_WithFallback_SendsIt()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateLanguageAsync(
                new CreateLanguageRequest
                {
                    IsoCode = "da-DK",
                    Name = "Danish",
                    FallbackIsoCode = "en-US",
                },
                CancellationToken.None
            );

        Assert.Equal(
            "en-US",
            handler.BodyOf(HttpMethod.Post, "/language")["fallbackIsoCode"]!.GetValue<string>()
        );
    }
}
