using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the migrated content write path (#79): create/update/publish/unpublish now go
/// through the generated client, resolving the document-type alias to an id and mapping
/// property values to <c>UntypedNode</c>. These drive the real <see cref="UmbracoManagementClient"/>
/// against <see cref="RoutingHandler"/> so the multi-step flows (tree walk -> get-by-id -> post
/// -> hydrate) can each return a distinct canned body and the wire requests can be asserted.
/// </summary>
public class ContentWriteClientTests
{
    /// <summary>
    /// A content create passes the document type by alias; the client must resolve it to an id
    /// (tree walk then by-id alias match), POST the document with the resolved id, and map property
    /// values to their JSON shape on the wire.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_ResolvesAliasAndPostsDocumentWithValues()
    {
        var docTypeId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document-type/root"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{docTypeId}}","name":"Text Page","isFolder":false}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"document-type/{docTypeId}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{docTypeId}}","alias":"textPage","name":"Text Page"}"""
            )
            .When(
                r => r.Method == HttpMethod.Post && HasPath(r, "/document"),
                HttpStatusCode.Created,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "/document/"),
                HttpStatusCode.OK,
                """{"id":"00000000-0000-0000-0000-000000000000","variants":[{"name":"Home"}]}"""
            );
        var client = Wire.Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                ContentType = new ContentTypeReference { Alias = "textPage" },
                Variants = [new ContentVariant { Name = "Home" }],
                Values = [new ContentValue { Alias = "bodyText", Value = "hello" }],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        // The POST body carries the resolved document-type id and the mapped property value.
        var postBody = handler.BodyForFirst(r =>
            r.Method == HttpMethod.Post && HasPath(r, "/document")
        );
        Assert.Contains(docTypeId.ToString(), postBody);
        Assert.Contains("\"bodyText\"", postBody);
        Assert.Contains("hello", postBody);
        // The create is hydrated by a follow-up GET, so the returned name is populated (#74).
        Assert.Equal("Home", result.Data!.Name);
    }

    /// <summary>
    /// #140: a client-supplied id must be posted verbatim (idempotent create), not replaced by a
    /// generated one, so re-provisioning with the same id targets the same document.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_WithSuppliedId_PostsThatId()
    {
        var docTypeId = Guid.NewGuid();
        var suppliedId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document-type/root"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{docTypeId}}","name":"Text Page","isFolder":false}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"document-type/{docTypeId}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{docTypeId}}","alias":"textPage","name":"Text Page"}"""
            )
            .When(
                r => r.Method == HttpMethod.Post && HasPath(r, "/document"),
                HttpStatusCode.Created,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "/document/"),
                HttpStatusCode.OK,
                $$"""{"id":"{{suppliedId}}","variants":[{"name":"Home"}]}"""
            );
        var client = Wire.Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                Id = suppliedId,
                ContentType = new ContentTypeReference { Alias = "textPage" },
                Variants = [new ContentVariant { Name = "Home" }],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var postBody = handler.BodyForFirst(r =>
            r.Method == HttpMethod.Post && HasPath(r, "/document")
        );
        Assert.Contains(suppliedId.ToString(), postBody);
    }

    /// <summary>
    /// Regression guard: the alias must be resolved WITHOUT the document-type item search. The
    /// search indexes only the type's name, so an alias that differs from the name by more than
    /// case ("Text Page" -> <c>textPage</c>, the Umbraco norm) is unfindable through it. An earlier
    /// revision of #79 searched and so could not create content for any such type.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_AliasDiffersFromName_ResolvesWithoutItemSearch()
    {
        var docTypeId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document-type/root"),
                HttpStatusCode.OK,
                // Name and alias share no substring, so only a by-id alias read can match.
                $$"""{"total":1,"items":[{"id":"{{docTypeId}}","name":"Vendor Hub Contact","isFolder":false}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"document-type/{docTypeId}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{docTypeId}}","alias":"vendorHubContact","name":"Vendor Hub Contact"}"""
            )
            .When(
                r => r.Method == HttpMethod.Post && HasPath(r, "/document"),
                HttpStatusCode.Created,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "/document/"),
                HttpStatusCode.OK,
                """{"id":"00000000-0000-0000-0000-000000000000","variants":[{"name":"C"}]}"""
            );
        var client = Wire.Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                ContentType = new ContentTypeReference { Alias = "vendorHubContact" },
                Variants = [new ContentVariant { Name = "C" }],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(
            docTypeId.ToString(),
            handler.BodyForFirst(r => r.Method == HttpMethod.Post && HasPath(r, "/document"))
        );
        Assert.DoesNotContain(
            handler.Requests,
            u =>
                u.AbsoluteUri.Contains(
                    "item/document-type/search",
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    /// <summary>
    /// A type nested inside a document-type folder is still resolvable: the tree walk recurses
    /// into folders, which the non-recursive tree root alone would miss.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_AliasInsideFolder_ResolvesViaTreeRecursion()
    {
        var folderId = Guid.NewGuid();
        var docTypeId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "tree/document-type/root"),
                HttpStatusCode.OK,
                // The root level holds only a folder - the type lives one level down.
                $$"""{"total":1,"items":[{"id":"{{folderId}}","name":"Pages","isFolder":true}]}"""
            )
            .When(
                r =>
                    r.Method == HttpMethod.Get
                    && Has(r, "tree/document-type/children")
                    && Has(r, folderId.ToString()),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{docTypeId}}","name":"Text Page","isFolder":false}]}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"document-type/{docTypeId}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{docTypeId}}","alias":"textPage","name":"Text Page"}"""
            )
            .When(
                r => r.Method == HttpMethod.Post && HasPath(r, "/document"),
                HttpStatusCode.Created,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "/document/"),
                HttpStatusCode.OK,
                """{"id":"00000000-0000-0000-0000-000000000000","variants":[{"name":"Home"}]}"""
            );
        var client = Wire.Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                ContentType = new ContentTypeReference { Alias = "textPage" },
                Variants = [new ContentVariant { Name = "Home" }],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(
            docTypeId.ToString(),
            handler.BodyForFirst(r => r.Method == HttpMethod.Post && HasPath(r, "/document"))
        );
    }

    /// <summary>
    /// An alias that resolves to no document type returns a clean 404 and never POSTs a document.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_UnknownAlias_IsInvalidArgumentAndDoesNotPost()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "tree/document-type/root"),
            HttpStatusCode.OK,
            """{"total":0,"items":[]}"""
        );
        var client = Wire.Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                ContentType = new ContentTypeReference { Alias = "nope" },
                Variants = [new ContentVariant { Name = "X" }],
            },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.DoesNotContain(handler.Requests, u => u.AbsolutePath.EndsWith("/document"));
    }

    /// <summary>
    /// Publishing PUTs to the publish endpoint. What the body must contain is covered in detail by
    /// <see cref="ContentPublishWireTests"/> - the assertion that used to live here
    /// (<c>Contains("*", body)</c>) passed against a body that published nothing, which is how
    /// #158 shipped green for two releases.
    /// </summary>
    [Fact]
    public async Task PublishContentAsync_PutsToThePublishEndpoint()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Wire.Client(handler);

        var result = await client.PublishContentAsync(id, ["en-US"], ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(
            handler.RequestFor(r => r.Method == HttpMethod.Put && Has(r, $"document/{id}/publish"))
        );
    }

    /// <summary>
    /// #90: a scheduled publish sends the publish and unpublish times on the per-culture schedule.
    /// (A schedule is only sent when one was asked for - an empty schedule object is the #158
    /// no-op, not "now / never".)
    /// </summary>
    [Fact]
    public async Task PublishContentAsync_WithSchedule_SendsPublishAndUnpublishTimes()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Wire.Client(handler);
        var publishAt = DateTimeOffset.Parse("2026-01-01T09:00:00Z");
        var unpublishAt = DateTimeOffset.Parse("2026-02-01T18:30:00Z");

        var result = await client.PublishContentAsync(
            id,
            null,
            publishAt,
            unpublishAt,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var body = handler.BodyForFirst(r => Has(r, $"document/{id}/publish"));
        Assert.Contains("2026-01-01", body); // publishTime
        Assert.Contains("2026-02-01", body); // unpublishTime
    }

    /// <summary>
    /// #90: publish-with-descendants surfaces the background task id and completion flag from the
    /// PUT response instead of discarding them.
    /// </summary>
    [Fact]
    public async Task PublishContentWithDescendantsAsync_SurfacesTaskId()
    {
        var id = Guid.NewGuid();
        var taskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Put && Has(r, "publish-with-descendants"),
            HttpStatusCode.OK,
            """{"taskId":"11111111-1111-1111-1111-111111111111","isComplete":false}"""
        );
        var client = Wire.Client(handler);

        var result = await client.PublishContentWithDescendantsAsync(
            id,
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(taskId, result.Data!.TaskId);
        Assert.False(result.Data!.IsComplete);
    }

    /// <summary>
    /// #90: with --wait the client polls the result endpoint until the task reports complete.
    /// </summary>
    [Fact]
    public async Task PublishContentWithDescendantsAsync_Wait_PollsResultUntilComplete()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Put && Has(r, "publish-with-descendants"),
                HttpStatusCode.OK,
                """{"taskId":"11111111-1111-1111-1111-111111111111","isComplete":false}"""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "publish-with-descendants/result"),
                HttpStatusCode.OK,
                """{"taskId":"11111111-1111-1111-1111-111111111111","isComplete":true}"""
            );
        var client = Wire.Client(handler);

        var result = await client.PublishContentWithDescendantsAsync(
            id,
            wait: true,
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsComplete);
        Assert.Contains(
            handler.Requests,
            u => u.AbsoluteUri.Contains("publish-with-descendants/result")
        );
    }

    /// <summary>
    /// With no cultures the unpublish body must OMIT the cultures field (unpublish the whole
    /// document) rather than send the <c>"*"</c> wildcard: unpublish, unlike publish, rejects
    /// <c>"*"</c> on an invariant document with HTTP 400. Regression for the alpha.8 finding
    /// <c>content.unpublish.invariant-culture</c>.
    /// </summary>
    [Fact]
    public async Task UnpublishContentAsync_NoCultures_OmitsCulturesSoInvariantDocsUnpublish()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Wire.Client(handler);

        var result = await client.UnpublishContentAsync(id, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var body = handler.BodyForFirst(r =>
            r.Method == HttpMethod.Put && Has(r, $"document/{id}/unpublish")
        );
        // No "*" wildcard is sent - that is the payload the invariant document rejects.
        Assert.DoesNotContain("*", body);
    }

    /// <summary>
    /// When specific cultures are given they are sent as the <c>culture</c> list verbatim (and no
    /// <c>"*"</c> wildcard is injected).
    /// </summary>
    [Fact]
    public async Task UnpublishContentAsync_WithCultures_SendsThoseCultures()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Wire.Client(handler);

        var result = await client.UnpublishContentAsync(id, ["en-US"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var body = handler.BodyForFirst(r =>
            r.Method == HttpMethod.Put && Has(r, $"document/{id}/unpublish")
        );
        Assert.Contains("cultures", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("en-US", body);
        Assert.DoesNotContain("*", body);
    }

    /// <summary>
    /// Update reads the document, PUTs it, then re-reads it, returning a hydrated payload. The
    /// merge behaviour that read enables is covered by <see cref="ContentUpdateMergeClientTests"/>.
    /// </summary>
    [Fact]
    public async Task UpdateContentAsync_PutsThenHydrates()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                """{"id":"00000000-0000-0000-0000-000000000000","variants":[{"name":"Renamed"}]}"""
            );
        var client = Wire.Client(handler);

        var result = await client.UpdateContentAsync(
            id,
            new UpdateContentRequest { Variants = [new ContentVariant { Name = "Renamed" }] },
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Data!.Name);
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith($"/document/{id}"));
    }

    private static bool Has(Recorded r, string fragment) =>
        r.Uri.AbsoluteUri.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool HasPath(Recorded r, string suffix) =>
        r.Uri.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Live-request overload used by route predicates.</summary>
    /// <param name="r">The in-flight request.</param>
    /// <param name="fragment">The URI fragment to look for.</param>
    /// <returns>True when the absolute URI contains the fragment.</returns>
    private static bool Has(HttpRequestMessage r, string fragment) =>
        r.RequestUri!.AbsoluteUri.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Live-request overload used by route predicates.</summary>
    /// <param name="r">The in-flight request.</param>
    /// <param name="suffix">The path suffix to look for.</param>
    /// <returns>True when the path ends with the suffix.</returns>
    private static bool HasPath(HttpRequestMessage r, string suffix) =>
        r.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
