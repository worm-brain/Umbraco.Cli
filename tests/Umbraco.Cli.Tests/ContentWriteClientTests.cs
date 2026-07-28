using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the migrated content write path (#79): create/update/publish/unpublish now go
/// through the generated client, resolving the document-type alias to an id and mapping
/// property values to <c>UntypedNode</c>. These drive the real <see cref="UmbracoManagementClient"/>
/// against <see cref="RoutingHandler"/> so the multi-step flows (search -> get-by-id -> post
/// -> hydrate) can each return a distinct canned body and the wire requests can be asserted.
/// </summary>
public class ContentWriteClientTests
{
    /// <summary>Builds a client whose HTTP calls are answered by <paramref name="handler"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <returns>A client bound to the handler.</returns>
    private static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    /// <summary>
    /// A content create passes the document type by alias; the client must resolve it to an id
    /// (search then by-id alias match), POST the document with the resolved id, and map property
    /// values to their JSON shape on the wire.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_ResolvesAliasAndPostsDocumentWithValues()
    {
        var docTypeId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Has(r, "item/document-type/search"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{docTypeId}}","name":"Text Page"}]}"""
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
        var client = Client(handler);

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
    /// An alias that resolves to no document type returns a clean 404 and never POSTs a document.
    /// </summary>
    [Fact]
    public async Task CreateContentAsync_UnknownAlias_Returns404AndDoesNotPost()
    {
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get && Has(r, "item/document-type/search"),
            HttpStatusCode.OK,
            """{"total":0,"items":[]}"""
        );
        var client = Client(handler);

        var result = await client.CreateContentAsync(
            new CreateContentRequest
            {
                ContentType = new ContentTypeReference { Alias = "nope" },
                Variants = [new ContentVariant { Name = "X" }],
            },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.DoesNotContain(handler.Requests, u => u.AbsolutePath.EndsWith("/document"));
    }

    /// <summary>
    /// Publishing with no cultures defaults to the <c>"*"</c> wildcard and PUTs to the publish
    /// endpoint with a schedule entry.
    /// </summary>
    [Fact]
    public async Task PublishContentAsync_DefaultsToAllCultures()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Client(handler);

        var result = await client.PublishContentAsync(id, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var put = handler.RequestFor(r =>
            r.Method == HttpMethod.Put && Has(r, $"document/{id}/publish")
        );
        Assert.NotNull(put);
        Assert.Contains("*", handler.BodyForFirst(r => Has(r, $"document/{id}/publish")));
    }

    /// <summary>
    /// Unpublishing sends a plain <c>cultures</c> list (distinct from publish's schedule payload),
    /// defaulting to the <c>"*"</c> wildcard.
    /// </summary>
    [Fact]
    public async Task UnpublishContentAsync_SendsCulturesList()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");
        var client = Client(handler);

        var result = await client.UnpublishContentAsync(id, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var body = handler.BodyForFirst(r =>
            r.Method == HttpMethod.Put && Has(r, $"document/{id}/unpublish")
        );
        Assert.Contains("cultures", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*", body);
    }

    /// <summary>
    /// Update PUTs the document then re-reads it, returning a hydrated payload.
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
        var client = Client(handler);

        var result = await client.UpdateContentAsync(
            id,
            new UpdateContentRequest { Variants = [new ContentVariant { Name = "Renamed" }] },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Data!.Name);
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith($"/document/{id}"));
    }

    private static bool Has(HttpRequestMessage r, string fragment) =>
        r.RequestUri!.AbsoluteUri.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool HasPath(HttpRequestMessage r, string suffix) =>
        r.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
