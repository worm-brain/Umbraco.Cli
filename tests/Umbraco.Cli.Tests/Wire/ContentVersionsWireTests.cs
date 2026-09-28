using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Version history across cultures (#209). Umbraco lists no versions of a culture-variant document
/// unless a culture is passed, so <c>content version list</c> with no <c>--culture</c> returned an
/// empty list that read as "no history". The client now lists every culture and tags each row.
/// </summary>
public class ContentVersionsWireTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    /// <summary>A version list body with one row per (id, date).</summary>
    /// <param name="total">The total the server reports.</param>
    /// <param name="rows">The (id, ISO date) of each row.</param>
    /// <returns>The JSON body.</returns>
    private static string Versions(int total, params (Guid Id, string Date)[] rows) =>
        $$"""{ "total": {{total}}, "items": [{{string.Join(
            ",",
            rows.Select(r => $$"""{ "id": "{{r.Id}}", "versionDate": "{{r.Date}}" }""")
        )}}] }""";

    /// <summary>
    /// A handler whose document GET has the given cultures and whose version list answers per
    /// <c>culture</c> query parameter (the unfiltered list is <paramref name="unfiltered"/>).
    /// </summary>
    /// <param name="documentCultures">The document's variant cultures; empty for invariant.</param>
    /// <param name="perCulture">The version body returned for each culture.</param>
    /// <param name="unfiltered">The version body returned with no culture.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Handler(
        string[] documentCultures,
        Dictionary<string, string> perCulture,
        string unfiltered = """{ "total": 0, "items": [] }"""
    )
    {
        var variants =
            documentCultures.Length == 0
                ? """[{ "culture": null, "name": "Home" }]"""
                : "["
                    + string.Join(
                        ",",
                        documentCultures.Select(c =>
                            $$"""{ "culture": "{{c}}", "name": "{{c}}" }"""
                        )
                    )
                    + "]";
        var handler = new RoutingHandler();
        foreach (var (culture, body) in perCulture)
            handler.When(
                r =>
                    r.RequestUri!.AbsolutePath.EndsWith("/document-version")
                    && r.RequestUri.Query.Contains($"culture={culture}"),
                HttpStatusCode.OK,
                body
            );
        return handler
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/document-version"),
                HttpStatusCode.OK,
                unfiltered
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/document/{DocumentId}"),
                HttpStatusCode.OK,
                $$"""{ "id": "{{DocumentId}}", "variants": {{variants}} }"""
            );
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_VariantDocumentWithNoCulture_MergesEveryCultureNewestFirst()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var handler = Handler(
            ["en-US", "da-DK"],
            new()
            {
                ["en-US"] = Versions(1, (older, "2026-01-01T00:00:00Z")),
                ["da-DK"] = Versions(1, (newer, "2026-02-01T00:00:00Z")),
            }
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, ct: CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            [(newer, "da-DK"), (older, "en-US")],
            result.Data!.Items.Select(v => (v.Id, v.Culture)).ToList()
        );
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_DraftAndPublishedShareADate_ListsTheDraftFirst()
    {
        // #294: publishing turns the draft into the published version and starts a new draft at
        // the same moment; the tie came out in whatever order the server returned.
        var published = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var handler = Handler(
            [],
            [],
            $$"""
            { "total": 2, "items": [
              { "id": "{{published}}", "versionDate": "2026-09-28T08:34:22Z", "isCurrentPublishedVersion": true },
              { "id": "{{draft}}", "versionDate": "2026-09-28T08:34:22Z", "isCurrentDraftVersion": true } ] }
            """
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, ct: CancellationToken.None);

        Assert.Equal([draft, published], result.Data!.Items.Select(v => v.Id).ToList());
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_VariantDocumentWithNoCulture_ReportsTheSummedTotal()
    {
        var handler = Handler(
            ["en-US", "da-DK"],
            new()
            {
                ["en-US"] = Versions(3, (Guid.NewGuid(), "2026-01-01T00:00:00Z")),
                ["da-DK"] = Versions(1, (Guid.NewGuid(), "2026-02-01T00:00:00Z")),
            }
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, ct: CancellationToken.None);

        Assert.Equal(4, result.Data!.Total);
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_MergedList_AppliesSkipAndTakeAfterMerging()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var handler = Handler(
            ["en-US", "da-DK"],
            new()
            {
                ["en-US"] = Versions(
                    2,
                    (first, "2026-03-01T00:00:00Z"),
                    (third, "2026-01-01T00:00:00Z")
                ),
                ["da-DK"] = Versions(1, (second, "2026-02-01T00:00:00Z")),
            }
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, skip: 1, take: 1, ct: CancellationToken.None);

        Assert.Equal(second, Assert.Single(result.Data!.Items).Id);
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_InvariantDocument_QueriesOnceWithoutACulture()
    {
        var id = Guid.NewGuid();
        var handler = Handler([], [], Versions(1, (id, "2026-01-01T00:00:00Z")));

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, ct: CancellationToken.None);

        var row = Assert.Single(result.Data!.Items);
        Assert.Equal((id, (string?)null), (row.Id, row.Culture));
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_ExplicitCulture_TagsRowsWithoutReadingTheDocument()
    {
        var handler = Handler(
            ["en-US", "da-DK"],
            new() { ["da-DK"] = Versions(1, (Guid.NewGuid(), "2026-01-01T00:00:00Z")) }
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionsAsync(DocumentId, "da-DK", ct: CancellationToken.None);

        Assert.Equal("da-DK", Assert.Single(result.Data!.Items).Culture);
        handler.AssertNoRequest(HttpMethod.Get, $"/document/{DocumentId}");
    }

    // ── one version (content version get <id>) ───────────────────────────────────

    [Fact]
    public async Task GetDocumentVersionAsync_ReturnsTheRawBodyWithItsValues()
    {
        var versionId = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                $"/document-version/{versionId}",
                """{ "values": [ { "alias": "title", "culture": "en-US", "value": "Old title" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.Equal("Old title", result.Data!["values"]![0]!["value"]!.GetValue<string>());
    }

    /// <summary>A version of <see cref="DocumentId"/> named "Old post", and the document's tree ancestors.</summary>
    /// <param name="versionId">The version id.</param>
    /// <param name="parentId">The document's parent.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler VersionWithParent(Guid versionId, Guid parentId) =>
        Wire.Routed(
            (
                $"/document-version/{versionId}",
                $$"""{ "document": { "id": "{{DocumentId}}" }, "variants": [ { "culture": null, "name": "Old post" } ] }"""
            ),
            (
                "/tree/document/ancestors",
                $$"""[ { "id": "{{parentId}}", "parent": null }, { "id": "{{DocumentId}}", "parent": { "id": "{{parentId}}" } } ]"""
            )
        );

    [Fact]
    public async Task GetDocumentVersionAsync_AddsTheFirstVariantsName()
    {
        // #315: content get and blueprint get have a top-level name; version get had none.
        var versionId = Guid.NewGuid();

        var result = await Wire.Client(VersionWithParent(versionId, Guid.NewGuid()))
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.Equal("Old post", result.Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetDocumentVersionAsync_AddsTheDocumentsParent()
    {
        var versionId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var result = await Wire.Client(VersionWithParent(versionId, parentId))
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.Equal(parentId.ToString(), result.Data!["parent"]!["id"]!.GetValue<string>());
    }

    /// <summary>A version whose document type is <paramref name="typeId"/>, and that type's read.</summary>
    /// <param name="versionId">The version id.</param>
    /// <param name="typeId">The document type id.</param>
    /// <param name="typeStatus">The status the document-type read answers with.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler VersionWithType(
        Guid versionId,
        Guid typeId,
        HttpStatusCode typeStatus = HttpStatusCode.OK
    ) =>
        new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/document-version/{versionId}"),
                HttpStatusCode.OK,
                $$"""{ "documentType": { "id": "{{typeId}}", "icon": "icon-document", "collection": null } }"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/document-type/{typeId}"),
                typeStatus,
                typeStatus == HttpStatusCode.OK ? """{ "alias": "blogPost" }""" : ""
            );

    [Fact]
    public async Task GetDocumentVersionAsync_AddsTheDocumentTypesAlias()
    {
        // #320: content get and list have documentType.alias; version get had none.
        var versionId = Guid.NewGuid();

        var result = await Wire.Client(VersionWithType(versionId, Guid.NewGuid()))
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.Equal("blogPost", result.Data!["documentType"]!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetDocumentVersionAsync_NullCollection_DropsTheKeyAsContentGetDoes()
    {
        var versionId = Guid.NewGuid();

        var result = await Wire.Client(VersionWithType(versionId, Guid.NewGuid()))
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.False(result.Data!["documentType"]!.AsObject().ContainsKey("collection"));
    }

    [Fact]
    public async Task GetDocumentVersionAsync_TypeUnreadable_StillReturnsTheVersionWithoutAnAlias()
    {
        var versionId = Guid.NewGuid();

        var result = await Wire.Client(
                VersionWithType(versionId, Guid.NewGuid(), HttpStatusCode.NotFound)
            )
            .GetDocumentVersionAsync(versionId, CancellationToken.None);

        Assert.False(result.Data!["documentType"]!.AsObject().ContainsKey("alias"));
    }

    [Fact]
    public async Task GetDocumentVersionAsync_UnknownVersion_ReturnsTheFailure()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.NotFound, "");

        var result = await Wire.Client(handler)
            .GetDocumentVersionAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    // The document a version belongs to (#233): what rollback --publish publishes.

    [Fact]
    public async Task GetVersionDocumentIdAsync_ReturnsTheVersionsDocument()
    {
        var handler = Wire.Returning($$"""{ "document": { "id": "{{DocumentId}}" } }""");

        var result = await Wire.Client(handler)
            .GetVersionDocumentIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DocumentId, result.Data);
    }

    [Fact]
    public async Task GetVersionDocumentIdAsync_NoDocumentInTheAnswer_IsAnUnexpectedResponse()
    {
        var handler = Wire.Returning("{}");

        var result = await Wire.Client(handler)
            .GetVersionDocumentIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
    }
}
