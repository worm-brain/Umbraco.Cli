using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The content and dictionary read model (round 3, Phase 4): <c>content get</c> carries every
/// field of <c>GET /document/{id}</c> under the API's keys (#306, #284, #297) plus the CLI's
/// <c>name</c>, <c>parent</c> and <c>urls</c> (#289); dictionary items say where they live (#290);
/// blueprints get the same <c>name</c> and <c>parent</c> as content (#298).
/// </summary>
public class ContentReadModelWireTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Type = Guid.NewGuid();

    /// <summary>The output writer's settings that matter here: nulls are left out.</summary>
    private static readonly JsonSerializerOptions Output = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A document with every field of <c>DocumentResponseModel</c> set.</summary>
    private static readonly string FullDocument = $$"""
        {
          "id": "{{Item}}",
          "isTrashed": true,
          "flags": [ { "alias": "Umb.ScheduledForPublish" } ],
          "documentType": { "id": "{{Type}}", "icon": "icon-document", "collection": { "id": "{{Parent}}" } },
          "template": { "id": "{{Parent}}" },
          "values": [ { "alias": "title", "culture": null, "segment": null, "editorAlias": "Umbraco.TextBox", "value": "Hi" } ],
          "variants": [ {
            "id": "{{Guid.NewGuid()}}",
            "culture": "en-US", "segment": "mobile", "name": "Post",
            "createDate": "2026-09-01T00:00:00+00:00", "updateDate": "2026-09-02T00:00:00+00:00",
            "state": "Draft", "publishDate": "2026-09-03T00:00:00+00:00",
            "scheduledPublishDate": "2026-10-01T08:38:20+00:00",
            "scheduledUnpublishDate": "2026-11-01T00:00:00+00:00",
            "flags": [ { "alias": "Umb.ScheduledForPublish" } ]
          } ]
        }
        """;

    /// <summary>The spec's property names for a model, read from the committed Management API spec.</summary>
    /// <param name="model">The schema name, e.g. <c>DocumentResponseModel</c>.</param>
    /// <returns>Its property names.</returns>
    private static IEnumerable<string> SpecProperties(string model)
    {
        var spec = JsonNode.Parse(
            File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "spec", "management.json"))
        )!;
        return spec["components"]!["schemas"]![model]!["properties"]!.AsObject().Select(p => p.Key);
    }

    /// <summary>Reads the full document through <c>GetContentByIdAsync</c> and serializes it as the CLI would.</summary>
    /// <returns>The serialized <c>data</c> object.</returns>
    private static async Task<JsonObject> GetFullDocumentAsync()
    {
        var handler = Wire.Routed(
            (
                "document/urls",
                $$"""[ { "id": "{{Item}}", "urlInfos": [ { "culture": "en-US", "url": "/post/", "message": null, "provider": "Umbraco" } ] } ]"""
            ),
            (
                "tree/document/ancestors",
                $$"""[ { "id": "{{Parent}}" }, { "id": "{{Item}}", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            ($"/document-type/{Type}", """{ "alias": "blogPost" }"""),
            ($"/document/{Item}", FullDocument)
        );
        var result = await Wire.Client(handler).GetContentByIdAsync(Item, CancellationToken.None);
        return JsonSerializer.SerializeToNode(result.Data, Output)!.AsObject();
    }

    [Fact]
    public async Task GetContentByIdAsync_EveryDocumentField_IsInTheOutput()
    {
        // #306 verification: every property the spec gives the by-id body appears in the output,
        // so a field Umbraco adds fails this test rather than being dropped.
        var data = await GetFullDocumentAsync();

        Assert.All(
            SpecProperties("DocumentResponseModel"),
            p => Assert.True(data.ContainsKey(p), p)
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_EveryVariantField_IsInTheOutput()
    {
        var variant = (await GetFullDocumentAsync())["variants"]![0]!.AsObject();

        Assert.All(
            SpecProperties("DocumentVariantResponseModel"),
            p => Assert.True(variant.ContainsKey(p), p)
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_EveryDocumentTypeField_IsInTheOutput()
    {
        var type = (await GetFullDocumentAsync())["documentType"]!.AsObject();

        Assert.All(
            SpecProperties("DocumentTypeReferenceResponseModel"),
            p => Assert.True(type.ContainsKey(p), p)
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_KeepsTheCliAddedFields()
    {
        var data = await GetFullDocumentAsync();

        Assert.Equal(
            ("Post", Parent.ToString(), "/post/", "blogPost"),
            (
                (string?)data["name"],
                (string?)data["parent"]!["id"],
                (string?)data["urls"]![0]!["url"],
                (string?)data["documentType"]!["alias"]
            )
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_PendingSchedule_ReportsTheDates()
    {
        // #297: the dates were in the output but never filled.
        var variant = (await GetFullDocumentAsync())["variants"]![0]!;

        Assert.Equal(
            ("2026-10-01T08:38:20+00:00", "2026-11-01T00:00:00+00:00"),
            ((string?)variant["scheduledPublishDate"], (string?)variant["scheduledUnpublishDate"])
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_UsesDocumentTypeNotContentType()
    {
        // #284: one key for the type across create, get, list and version get.
        var data = await GetFullDocumentAsync();

        Assert.Equal(
            (true, false),
            (data.ContainsKey("documentType"), data.ContainsKey("contentType"))
        );
    }

    [Fact]
    public async Task GetContentByIdAsync_UrlsReadFails_StillReturnsTheDocumentWithoutUrls()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.Contains("document/urls"),
                HttpStatusCode.InternalServerError,
                ""
            )
            .When(
                _ => true,
                HttpStatusCode.OK,
                $$"""{ "id": "{{Item}}", "variants": [ { "name": "Post" } ] }"""
            );

        var result = await Wire.Client(handler).GetContentByIdAsync(Item, CancellationToken.None);

        Assert.Equal((true, (object?)null), (result.IsSuccess, (object?)result.Data!.Urls));
    }

    [Fact]
    public async Task GetContentAsync_TreeRows_UseTheSameKeysAsGet()
    {
        var handler = Wire.Routed(
            ($"/document-type/{Type}", """{ "alias": "blogPost" }"""),
            (
                "tree/document/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "isTrashed": false, "documentType": { "id": "{{Type}}", "icon": "icon-document" }, "variants": [ { "name": "Post" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler).GetContentAsync(ct: CancellationToken.None);
        var row = JsonSerializer.SerializeToNode(Assert.Single(result.Data!.Items), Output)!;

        Assert.Equal(
            ("blogPost", "icon-document", false),
            (
                (string?)row["documentType"]!["alias"],
                (string?)row["documentType"]!["icon"],
                row.AsObject().ContainsKey("contentType")
            )
        );
    }

    // ── dictionary (#290) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDictionaryItemByKeyAsync_ChildItem_ReportsItsParent()
    {
        var handler = Wire.Routed(
            (
                "tree/dictionary/ancestors",
                $$"""[ { "id": "{{Parent}}", "name": "Blog" }, { "id": "{{Item}}", "name": "Blog.Probe", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            (
                $"/dictionary/{Item}",
                $$"""{ "id": "{{Item}}", "name": "Blog.Probe", "translations": [] }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetDictionaryItemByKeyAsync(Item.ToString(), CancellationToken.None);

        Assert.Equal(Parent, result.Data!.Parent!.Id);
    }

    [Fact]
    public async Task GetDictionaryItemByKeyAsync_AncestorsReadFails_StillReturnsTheItem()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.Contains("ancestors"),
                HttpStatusCode.InternalServerError,
                ""
            )
            .When(
                _ => true,
                HttpStatusCode.OK,
                $$"""{ "id": "{{Item}}", "name": "Blog.Probe", "translations": [] }"""
            );

        var result = await Wire.Client(handler)
            .GetDictionaryItemByKeyAsync(Item.ToString(), CancellationToken.None);

        Assert.Equal(("Blog.Probe", (Guid?)null), (result.Data!.Name, result.Data.Parent?.Id));
    }

    [Fact]
    public async Task CreateDictionaryItemAsync_UnderAParent_ReturnsTheParentAsRead()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "tree/dictionary/ancestors",
                $$"""[ { "id": "{{Parent}}" }, { "id": "{{id}}", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            (
                $"/dictionary/{id}",
                $$"""{ "id": "{{id}}", "name": "Blog.Probe", "translations": [] }"""
            )
        );

        var result = await Wire.Client(handler)
            .CreateDictionaryItemAsync(
                new CreateDictionaryItemRequest
                {
                    Id = id,
                    Name = "Blog.Probe",
                    Parent = new ContentParentReference { Id = Parent },
                },
                CancellationToken.None
            );

        Assert.Equal(Parent, result.Data!.Parent!.Id);
    }

    [Fact]
    public async Task GetDictionaryItemsAsync_Rows_CarryTheOverviewParent()
    {
        var handler = Wire.Returning(
            $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "name": "Blog.Probe", "parent": { "id": "{{Parent}}" } } ] }"""
        );

        var result = await Wire.Client(handler).GetDictionaryItemsAsync(ct: CancellationToken.None);

        Assert.Equal(Parent, Assert.Single(result.Data!.Items).Parent!.Id);
    }

    // ── document blueprints (#298) ───────────────────────────────────────────

    [Fact]
    public async Task GetDocumentBlueprintAsync_InAFolder_AddsNameAndParent()
    {
        var handler = Wire.Routed(
            (
                "tree/document-blueprint/ancestors",
                $$"""[ { "id": "{{Parent}}", "isFolder": true }, { "id": "{{Item}}", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            (
                $"/document-blueprint/{Item}",
                $$"""{ "id": "{{Item}}", "documentType": { "id": "{{Type}}" }, "variants": [ { "culture": "en-US", "name": "Blog post starter" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetDocumentBlueprintAsync(Item, CancellationToken.None);

        Assert.Equal(
            ("Blog post starter", Parent.ToString()),
            ((string?)result.Data!["name"], (string?)result.Data["parent"]!["id"])
        );
    }

    [Fact]
    public async Task GetDocumentBlueprintAsync_AtTheRoot_HasNoParent()
    {
        var handler = Wire.Routed(
            ("tree/document-blueprint/ancestors", $$"""[ { "id": "{{Item}}" } ]"""),
            (
                $"/document-blueprint/{Item}",
                $$"""{ "id": "{{Item}}", "variants": [ { "name": "Starter" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetDocumentBlueprintAsync(Item, CancellationToken.None);

        Assert.Equal(
            ("Starter", false),
            ((string?)result.Data!["name"], result.Data.AsObject().ContainsKey("parent"))
        );
    }

    [Fact]
    public async Task GetDocumentBlueprintAsync_NotFound_ReturnsTheFailure()
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.NotFound,
            """{ "title": "Not found" }"""
        );

        var result = await Wire.Client(handler)
            .GetDocumentBlueprintAsync(Item, CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
