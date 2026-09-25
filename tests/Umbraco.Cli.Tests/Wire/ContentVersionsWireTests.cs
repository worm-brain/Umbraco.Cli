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

    [Fact]
    public async Task GetDocumentVersionAsync_UnknownVersion_ReturnsTheFailure()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.NotFound, "");

        var result = await Wire.Client(handler)
            .GetDocumentVersionAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
