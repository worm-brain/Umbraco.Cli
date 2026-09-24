using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Reading a type by the key a human has (#159) and creating a dictionary item without lying
/// about what was saved (#181), plus the member group filter (#184) and the language fallback
/// (#183). Three of these were silent wrong answers: a filter that matched nothing, a create that
/// reported translations Umbraco had discarded, and two <c>get</c> commands that rejected the key
/// their own documentation told you to use.
/// </summary>
public class TypeLookupAndDictionaryTests
{
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
        var handler = Wire.Routed(
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
        var handler = Wire.Routed(
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
        var handler = Wire.Routed(("item/data-type/search", """{ "total": 0, "items": [] }"""));

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
        var handler = Wire.Routed(
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
        var handler = Wire.Routed(
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
    public async Task CreateDictionaryItemAsync_UnreadableLanguageList_RefusesRatherThanGuessing()
    {
        var handler = Wire.Routed(("/language", """{ "total": 0, "items": [] }"""));

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

        // Umbraco always has at least one language, so an empty list means the read did not work.
        // Treating that as "nothing to check" would quietly restore the silent-discard behaviour
        // this guard exists to end, so it refuses and says why instead.
        Assert.False(result.IsSuccess);
        Assert.Contains("Could not read", result.ErrorMessage);
        handler.AssertNoRequest(HttpMethod.Post, "/dictionary");
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

    [Fact]
    public async Task CreateDictionaryItemAsync_ReturnsWhatTheInstanceStored_NotTheRequest()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var handler = Wire.Routed(
            (
                "/language",
                """{ "total": 2, "items": [ { "isoCode": "en-US" }, { "isoCode": "da-DK" } ] }"""
            ),
            (
                $"/dictionary/{id}",
                $$"""
                {
                  "id": "{{id}}",
                  "name": "Blog.ReadMore",
                  "translations": [ { "isoCode": "en-US", "translation": "Read more" } ]
                }
                """
            )
        );

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Id = id,
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "en-US", Translation = "Read more" },
                        new DictionaryTranslation { IsoCode = "da-DK", Translation = "Laes mere" },
                    ],
                },
                CancellationToken.None
            );

        // The request named two translations; the instance kept one. The response must report the
        // instance's answer - reporting the request back is the #181 bug.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var kept = Assert.Single(result.Data!.Translations!);
        Assert.Equal("en-US", kept.IsoCode);
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_ReadBackFails_DoesNotFabricateTranslations()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsoluteUri.Contains("/language"),
                System.Net.HttpStatusCode.OK,
                """{ "total": 1, "items": [ { "isoCode": "en-US" } ] }"""
            )
            .When(r => r.Method == HttpMethod.Get, System.Net.HttpStatusCode.NotFound, "")
            .When(_ => true, System.Net.HttpStatusCode.OK, "");

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Name = "Blog.ReadMore",
                    Translations =
                    [
                        new DictionaryTranslation { IsoCode = "en-US", Translation = "Read more" },
                    ],
                },
                CancellationToken.None
            );

        // The write succeeded, so this is still a success - but with no translations rather than
        // the request echoed back as though it had been confirmed.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(result.Data!.Translations);
    }
}
