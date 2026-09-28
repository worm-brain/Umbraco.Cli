using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

public class UmbracoManagementClientTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw _ex;
    }

    /// <summary>
    /// Test double that returns a canned JSON body + status for every request and records
    /// the last requested URI, so tests can assert both the endpoint the client called
    /// (the subject of issue #39) and how the response is mapped.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        private readonly string? _location;

        /// <summary>The absolute URI of the most recent request the client made.</summary>
        public Uri? LastRequestUri { get; private set; }

        /// <summary>Every request URI the client made, in order (for multi-step flows like upload).</summary>
        public List<Uri> Requests { get; } = [];

        /// <summary>Each request's body text, in order (null for bodiless requests). Lets tests
        /// assert what a read-merge PUT actually sends.</summary>
        public List<string?> RequestBodies { get; } = [];

        /// <param name="json">The response body to return.</param>
        /// <param name="status">The HTTP status to return (defaults to 200 OK).</param>
        /// <param name="location">Optional Location response header (for create tests).</param>
        public StubHandler(
            string json,
            HttpStatusCode status = HttpStatusCode.OK,
            string? location = null
        )
        {
            _json = json;
            _status = status;
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastRequestUri = request.RequestUri;
            if (request.RequestUri is not null)
                Requests.Add(request.RequestUri);
            // Capture the request body so read-merge tests can assert what a PUT actually sends.
            // Multipart (upload) bodies are captured too; tests only inspect JSON ones.
            RequestBodies.Add(
                request.Content is null
                    ? null
                    : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult()
            );
            var message = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            };
            if (_location is not null)
                message.Headers.Location = new Uri(_location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(message);
        }
    }

    private static UmbracoManagementClient ClientThatThrows(Exception ex) =>
        new(
            new HttpClient(new ThrowingHandler(ex))
            {
                BaseAddress = new Uri("https://example.com/"),
            }
        );

    private static (UmbracoManagementClient Client, StubHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        string? location = null
    )
    {
        var handler = new StubHandler(json, status, location);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    [Fact]
    public async Task TransportFailure_BecomesFailureNotException()
    {
        var client = ClientThatThrows(new HttpRequestException("no such host"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("reach", result.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_BecomesFailure()
    {
        // HttpClient surfaces a timeout as TaskCanceledException with no caller cancellation.
        var client = ClientThatThrows(new TaskCanceledException("timeout"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task GenuineCancellation_Propagates()
    {
        var client = ClientThatThrows(new HttpRequestException("unused"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetContentByIdAsync(Guid.NewGuid(), cts.Token)
        );
    }

    [Fact]
    public async Task TransportFailure_CategorisedUnreachable()
    {
        // #152: a transport failure (no HTTP response) is categorised Unreachable, distinct from a
        // timeout, so a caller can tell a connectivity problem from a server one.
        var client = ClientThatThrows(new HttpRequestException("no such host"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FailureCategory.Unreachable, result.Category);
    }

    [Fact]
    public async Task Timeout_CategorisedTimeout()
    {
        // #152: a timeout carries the same status 0 as an unreachable host, so the category (not
        // the code) is what distinguishes them.
        var client = ClientThatThrows(new TaskCanceledException("timeout"));

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FailureCategory.Timeout, result.Category);
    }

    [Fact]
    public async Task ApiError_4xx_CategorisedRequestRejected()
    {
        // #152: a 4xx is the server rejecting the request - the request/CLI's side, not a server fault.
        var (client, _) = ClientReturning("""{"title":"Not Found"}""", HttpStatusCode.NotFound);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.Equal(FailureCategory.RequestRejected, result.Category);
    }

    [Fact]
    public async Task ApiError_5xx_CategorisedServerError()
    {
        // #152: a 5xx (here an undeclared 500) is a server-side fault, so it is categorised
        // ServerError - the attribution that would have told us #150 was Umbraco's bug, not ours.
        var (client, _) = ClientReturning("", HttpStatusCode.InternalServerError);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.Equal(FailureCategory.ServerError, result.Category);
    }

    [Fact]
    public async Task GetServerVersionAsync_ReturnsVersionAndCachesTheLookup()
    {
        // #152: the version is fetched from server/information and cached, so annotating several
        // errors in one process (e.g. a bulk run) never re-fetches it.
        var (client, handler) = ClientReturning(
            """{"version":"17.3.5","assemblyVersion":"17.3.5.0"}"""
        );

        var first = await client.GetServerVersionAsync(CancellationToken.None);
        var second = await client.GetServerVersionAsync(CancellationToken.None);

        Assert.Equal("17.3.5", first);
        Assert.Equal("17.3.5", second);
        Assert.Single(handler.Requests); // resolved once, then served from cache
    }

    [Fact]
    public async Task GetServerVersionAsync_UnreachableServer_ReturnsNullWithoutThrowing()
    {
        // The lookup is best-effort: an unreachable server yields null, never an exception, so it
        // can never mask or delay the real error it was meant to annotate.
        var client = ClientThatThrows(new HttpRequestException("no such host"));

        var version = await client.GetServerVersionAsync(CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetContentAsync_NoParent_CallsTreeRootEndpoint()
    {
        // Regression for #39: root listing must hit the document tree, not the flat
        // (non-existent) /document collection.
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tree/document/root", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetContentAsync_FlattensVariantNameAndPublishedState()
    {
        // Regression for #39/#42: display name and published flag live under variants[],
        // not at the top level, and must be surfaced on the mapped item.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "total": 1,
              "items": [
                {
                  "id": "{{id}}",
                  "createDate": "2024-01-02T03:04:05+00:00",
                  "documentType": { "id": "11111111-1111-1111-1111-111111111111" },
                  "variants": [ { "culture": null, "name": "Home", "state": "Published" } ]
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("Home", item.Name);
        Assert.True(item.IsPublished);
    }

    [Fact]
    public async Task GetMediaTypesAsync_CallsTreeRootEndpoint()
    {
        // #55: media types list from the media-type tree root, mirroring document types
        // (there is no flat /media-type collection endpoint).
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetMediaTypesAsync(ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tree/media-type/root", handler.LastRequestUri!.AbsoluteUri);
    }

    /// <summary>Routes tree responses by URL so a folder walk can be exercised (#97).</summary>
    private sealed class TreeRouteHandler(Func<Uri, string> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        route(request.RequestUri!),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    private static UmbracoManagementClient TreeRouteClient(Func<Uri, string> route) =>
        new(
            new HttpClient(new TreeRouteHandler(route))
            {
                BaseAddress = new Uri("https://example.com/"),
            }
        );

    [Fact]
    public async Task GetDocumentTypesAsync_ExcludesFolders_AndIncludesNestedTypes()
    {
        // #97: the tree groups types into folders whose ids 404 on `get`. The walk must skip the
        // folder container yet surface the type nested inside it.
        var rootTypeId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var nestedTypeId = Guid.NewGuid();
        var client = TreeRouteClient(uri =>
            uri.AbsolutePath.EndsWith("/tree/document-type/root")
                ? $$"""{"total":2,"items":[{"id":"{{rootTypeId}}","name":"Text Page","isFolder":false,"hasChildren":false},{"id":"{{folderId}}","name":"Pages","isFolder":true,"hasChildren":true}]}"""
            : uri.Query.Contains(folderId.ToString())
                ? $$"""{"total":1,"items":[{"id":"{{nestedTypeId}}","name":"Landing","isFolder":false,"hasChildren":false}]}"""
            : """{"total":0,"items":[]}"""
        );

        var result = await client.GetDocumentTypesAsync(take: 100, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ids = result.Data!.Items.Select(i => i.Id).ToList();
        Assert.Contains(rootTypeId, ids); // root-level type
        Assert.Contains(nestedTypeId, ids); // type nested inside the folder
        Assert.DoesNotContain(folderId, ids); // the folder itself is excluded
        Assert.Equal(2, result.Data.Total); // Total counts real types, not folders
    }

    [Fact]
    public async Task GetMediaTypesAsync_ExcludesFolders_AndIncludesNestedTypes()
    {
        var rootTypeId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var nestedTypeId = Guid.NewGuid();
        var client = TreeRouteClient(uri =>
            uri.AbsolutePath.EndsWith("/tree/media-type/root")
                ? $$"""{"total":2,"items":[{"id":"{{rootTypeId}}","name":"Image","isFolder":false,"hasChildren":false},{"id":"{{folderId}}","name":"Assets","isFolder":true,"hasChildren":true}]}"""
            : uri.Query.Contains(folderId.ToString())
                ? $$"""{"total":1,"items":[{"id":"{{nestedTypeId}}","name":"Video","isFolder":false,"hasChildren":false}]}"""
            : """{"total":0,"items":[]}"""
        );

        var result = await client.GetMediaTypesAsync(take: 100, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ids = result.Data!.Items.Select(i => i.Id).ToList();
        Assert.Contains(rootTypeId, ids);
        Assert.Contains(nestedTypeId, ids);
        Assert.DoesNotContain(folderId, ids);
    }

    [Fact]
    public async Task CreateWebhookAsync_WithNameAndDescription_EchoesAndSendsThem()
    {
        // #80: name/description are sent in the request body and echoed on the response (the
        // 201 has an empty body, so the echo is what the command reports).
        // The create checks its events against GET webhook/events first (#234).
        var routes = Wire.Routed(
            ("/webhook/events", """{ "items": [ { "alias": "ContentPublished" } ] }""")
        );
        var client = Wire.Client(routes);

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest
            {
                Url = "https://example.com/hook",
                Events = ["ContentPublished"],
                Name = "My Hook",
                Description = "Fires on publish",
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("My Hook", result.Data!.Name);
        Assert.Equal("Fires on publish", result.Data.Description);
        Assert.Contains("My Hook", routes.RawBodyOf(HttpMethod.Post, "/webhook")); // sent, not just echoed
    }

    [Fact]
    public async Task CreateWebhookAsync_WithSuppliedId_UsesThatId()
    {
        // #86: a caller-supplied id enables idempotent creates — it must be used verbatim
        // (and appear in the request body) rather than a fresh GUID being generated.
        var id = Guid.NewGuid();
        // The create checks its events against GET webhook/events first (#234).
        var routes = Wire.Routed(
            ("/webhook/events", """{ "items": [ { "alias": "ContentPublished" } ] }""")
        );
        var client = Wire.Client(routes);

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest
            {
                Id = id,
                Url = "https://example.com/hook",
                Events = ["ContentPublished"],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Data!.Id);
        Assert.Contains(id.ToString(), routes.RawBodyOf(HttpMethod.Post, "/webhook"));
    }

    [Fact]
    public async Task CreateMediaTypeAsync_WithSuppliedId_UsesThatId()
    {
        // #86: media-type create honours a caller-supplied id.
        var id = Guid.NewGuid();
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateMediaTypeAsync(
            new CreateMediaTypeRequest
            {
                Id = id,
                Name = "Custom Image",
                Alias = "customImage",
            },
            CancellationToken.None
        );

        Assert.Equal(id, result.Data);
    }

    [Fact]
    public async Task CreateMediaTypeAsync_201EmptyBody_ReturnsAGeneratedId()
    {
        // #55: the client supplies the id up front, so a 201 with an empty body still reports
        // success with a usable id (guards #74). Only the id: #314 stopped echoing the request.
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateMediaTypeAsync(
            new CreateMediaTypeRequest { Name = "Custom Image", Alias = "customImage" },
            CancellationToken.None
        );

        Assert.NotEqual(Guid.Empty, result.Data);
    }

    // ── Content / media workflow (#67) ─────────────────────────────────────────

    [Fact]
    public async Task TrashContentAsync_CallsMoveToRecycleBinEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.TrashContentAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"document/{id}/move-to-recycle-bin", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task RestoreContentAsync_CallsRecycleBinRestoreEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.RestoreContentAsync(id, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"recycle-bin/document/{id}/restore", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task EmptyContentRecycleBinAsync_CallsDeleteRecycleBinEndpoint()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.EmptyContentRecycleBinAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            "/umbraco/management/api/v1/recycle-bin/document",
            handler.LastRequestUri!.AbsolutePath
        );
    }

    [Fact]
    public async Task MoveContentAsync_CallsMoveEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.MoveContentAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/document/{id}/move", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CopyContentAsync_CallsCopyEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CopyContentAsync(id, ct: CancellationToken.None);

        // The copy endpoint is hit. This stub returns no Location header, so the client reports the
        // new id could not be resolved (#91) - a case that cannot occur against a real server, where
        // the 201 always carries the Location. Full success is covered by ContentCopyClientTests.
        Assert.EndsWith($"/document/{id}/copy", handler.LastRequestUri!.AbsolutePath);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PublishContentWithDescendantsAsync_CallsPublishWithDescendantsEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.PublishContentWithDescendantsAsync(
            id,
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(
            $"document/{id}/publish-with-descendants",
            handler.LastRequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task TrashMediaAsync_CallsMoveToRecycleBinEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.TrashMediaAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"media/{id}/move-to-recycle-bin", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task RestoreMediaAsync_CallsRecycleBinRestoreEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.RestoreMediaAsync(id, RestoreTarget.Root, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"recycle-bin/media/{id}/restore", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task EmptyMediaRecycleBinAsync_CallsDeleteRecycleBinEndpoint()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.EmptyMediaRecycleBinAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            "/umbraco/management/api/v1/recycle-bin/media",
            handler.LastRequestUri!.AbsolutePath
        );
    }

    [Fact]
    public async Task MoveMediaAsync_CallsMoveEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.MoveMediaAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/media/{id}/move", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_CallsDocumentVersionEndpointWithDocumentId()
    {
        // #58: version history is read from GET /document-version filtered by documentId.
        var docId = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetDocumentVersionsAsync(docId, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("document-version", handler.LastRequestUri!.AbsoluteUri);
        Assert.Contains(docId.ToString(), handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetDocumentVersionsAsync_MapsVersionFields()
    {
        // #58: the current-draft flag and version date must be surfaced onto the mapped item.
        var versionId = Guid.NewGuid();
        var json = $$"""
            {
              "total": 1,
              "items": [
                {
                  "id": "{{versionId}}",
                  "versionDate": "2024-05-06T07:08:09+00:00",
                  "isCurrentDraftVersion": true,
                  "isCurrentPublishedVersion": false,
                  "preventCleanup": false
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetDocumentVersionsAsync(
            Guid.NewGuid(),
            ct: CancellationToken.None
        );

        var version = Assert.Single(result.Data!.Items);
        Assert.Equal(versionId, version.Id);
        Assert.True(version.IsCurrentDraftVersion);
        Assert.Equal(2024, version.VersionDate.Year);
    }

    [Fact]
    public async Task RollbackDocumentVersionAsync_PostsToRollbackEndpoint()
    {
        // #58: rollback targets POST /document-version/{versionId}/rollback.
        var versionId = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.RollbackDocumentVersionAsync(
            versionId,
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(
            $"document-version/{versionId}/rollback",
            handler.LastRequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task UploadMediaAsync_StagesToTemporaryFileThenCreatesMedia()
    {
        // #57: the upload must stage the bytes to temporary-file first, then create the media
        // item (a media type id is passed, so no resolution request is made). Since #172 it then
        // re-reads the created item, so the caller gets its URL and file metadata rather than an
        // echo of the request.
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await client.UploadMediaAsync(
            parentId: Guid.Empty,
            name: "Logo",
            fileStream: stream,
            fileName: "logo.png",
            contentType: "image/png",
            mediaType: Guid.NewGuid().ToString(),
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("Logo", result.Data.Name);
        // In order: stage the file, create the media item, then re-read it (#172) - the by-id
        // body for the values, and media/urls for the public URL, which is not on that body.
        Assert.Contains("temporary-file", handler.Requests[0].AbsoluteUri);
        Assert.EndsWith("/umbraco/management/api/v1/media", handler.Requests[1].AbsolutePath);
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith("/media/urls"));
    }

    [Fact]
    public async Task UploadMediaAsync_UnknownMediaTypeName_IsInvalidArgument()
    {
        // A media type name that matches nothing yields a clear 404 rather than staging a file
        // that can never be attached.
        var (client, _) = ClientReturning("""{"total":0,"items":[]}""");
        using var stream = new MemoryStream(new byte[] { 1 });

        var result = await client.UploadMediaAsync(
            Guid.Empty,
            "X",
            stream,
            "x.bin",
            "application/octet-stream",
            "NoSuchType",
            ct: CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }

    [Fact]
    public async Task GetMemberTypesAsync_CallsTreeRootEndpoint()
    {
        // #56: member types list from the member-type tree root (no flat /member-type collection).
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetMemberTypesAsync(ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tree/member-type/root", handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateMemberTypeAsync_201EmptyBody_ReturnsAGeneratedId()
    {
        // #56: the client supplies the id up front, so a 201 with an empty body still reports
        // success with a usable id. Only the id: #314 stopped echoing the request.
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateMemberTypeAsync(
            new CreateMemberTypeRequest { Name = "Author", Alias = "author" },
            CancellationToken.None
        );

        Assert.NotEqual(Guid.Empty, result.Data);
    }

    [Fact]
    public async Task CreateTemplateAsync_201EmptyBody_EchoesRequestWithGeneratedId()
    {
        // #59: template create supplies a client id and echoes the request on the empty 201.
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateTemplateAsync(
            new CreateTemplateRequest { Name = "Home", Alias = "home" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("home", result.Data.Alias);
    }

    [Fact]
    public async Task CreateDataTypeAsync_201EmptyBody_ReturnsAGeneratedId()
    {
        // #59: data-type create supplies a client id, so the empty 201 still yields a usable id.
        // Only the id: #314 stopped echoing the request.
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateDataTypeAsync(
            new CreateDataTypeRequest
            {
                Name = "My Text",
                EditorAlias = "Umbraco.TextBox",
                EditorUiAlias = "Umb.PropertyEditorUi.TextBox",
            },
            CancellationToken.None
        );

        Assert.NotEqual(Guid.Empty, result.Data);
    }

    [Fact]
    public async Task DeleteDictionaryItemAsync_CallsDeleteEndpoint()
    {
        // #59: dictionary delete targets DELETE /dictionary/{id}.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteDictionaryItemAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The by-id read is followed by the parent lookup (#290), so it is not the last request.
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith($"/dictionary/{id}"));
    }

    [Fact]
    public async Task UpdateLanguageAsync_ReadMergesAndPreservesUnsuppliedFields()
    {
        // #59 + review fix: language update is read-merge. Changing only the name must PRESERVE
        // the current isDefault/isMandatory/fallback rather than clearing them. The stub returns
        // the current language for the GET; the PUT body is asserted to carry the preserved flags.
        var json = """
            {
              "isoCode": "en-US",
              "name": "English",
              "isDefault": true,
              "isMandatory": true,
              "fallbackIsoCode": "en"
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.UpdateLanguageAsync(
            "en-US",
            new UpdateLanguageRequest { Name = "English (US)" }, // only the name changes
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("English (US)", result.Data!.Name);
        Assert.True(result.Data.IsMandatory); // preserved
        Assert.True(result.Data.IsDefault); // preserved
        // Two requests: read then write; the PUT body carries the preserved flags + new name.
        Assert.Equal(2, handler.Requests.Count);
        var putBody = handler.RequestBodies[1]!;
        Assert.Contains("English (US)", putBody);
        Assert.Contains("\"isMandatory\":true", putBody);
        Assert.Contains("\"isDefault\":true", putBody);
    }

    [Fact]
    public async Task UpdateTemplateAsync_ReadMergesAndPreservesContentWhenNotSupplied()
    {
        // Review fix: updating a template's name must NOT blank its Razor content. The client
        // reads the current template and preserves content when the request's content is null.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "name": "Old Name",
              "alias": "oldAlias",
              "content": "@* the razor body *@"
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.UpdateTemplateAsync(
            id,
            new UpdateTemplateRequest { Name = "New Name" }, // content omitted
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count); // GET then PUT
        var putBody = handler.RequestBodies[1]!;
        Assert.Contains("New Name", putBody);
        Assert.Contains("the razor body", putBody); // content preserved, not blanked
        Assert.Contains("oldAlias", putBody); // alias preserved
    }

    [Fact]
    public async Task UpdateDataTypeAsync_ReadMergesAndPreservesEditorValues()
    {
        // Review fix: updating a data type's name must NOT wipe its editor configuration values.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "name": "Old",
              "editorAlias": "Umbraco.TextBox",
              "editorUiAlias": "Umb.PropertyEditorUi.TextBox",
              "values": [ { "alias": "maxChars", "value": 200 } ]
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.UpdateDataTypeAsync(
            id,
            new UpdateDataTypeRequest { Name = "New" }, // editor + values omitted
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count); // GET then PUT
        var putBody = handler.RequestBodies[1]!;
        Assert.Contains("New", putBody);
        Assert.Contains("maxChars", putBody); // editor config value preserved
        Assert.Contains("Umbraco.TextBox", putBody); // editor alias preserved
    }

    [Fact]
    public async Task UpdateTemplateAsync_MissingTemplate_IsInvalidArgument()
    {
        // The read step surfaces a clean 404 when the template does not exist.
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.UpdateTemplateAsync(
            Guid.NewGuid(),
            new UpdateTemplateRequest { Name = "X" },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteTemplateAsync_CallsDeleteEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteTemplateAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/template/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteDataTypeAsync_CallsDeleteEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteDataTypeAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/data-type/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteMediaTypeAsync_CallsDeleteEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteMediaTypeAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/media-type/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteMemberTypeAsync_CallsDeleteEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteMemberTypeAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/member-type/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UploadMediaAsync_UnreadableCreatedItem_StillEchoesIdAndName()
    {
        // #79/#172: the media create is a void POST (empty 201; Kiota does not surface the
        // Location header), so the id is client-generated. Since #172 the client re-reads the
        // created item to return its URL and file metadata - but a failed re-read must not fail
        // the upload, which did succeed. This handler answers everything with an empty body, so
        // the hydration finds nothing and the fabricated echo is the fallback.
        var handler = new StubHandler("", HttpStatusCode.Created);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        using var stream = new MemoryStream(new byte[] { 1 });

        var result = await client.UploadMediaAsync(
            null,
            "Logo",
            stream,
            "logo.png",
            "image/png",
            Guid.NewGuid().ToString(),
            ct: CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Logo", result.Data!.Name); // echoed, not blanked (#74)
        Assert.NotEqual(Guid.Empty, result.Data.Id); // client-generated id
    }

    [Fact]
    public async Task UpdateMemberAsync_ReadsThenMergesChangesOverCurrentMember()
    {
        // #59: member update is read-modify-write — it must GET the member first, then PUT. The
        // supplied name overrides the variant name while the email (not supplied) is preserved.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "email": "old@example.com",
              "username": "olduser",
              "isApproved": true,
              "variants": [ { "culture": null, "name": "Old Name" } ],
              "groups": [],
              "values": []
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.UpdateMemberAsync(
            id,
            new UpdateMemberRequest { Name = "New Name" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Data!.Name);
        Assert.Equal("old@example.com", result.Data.Email); // preserved (not supplied)
        // Two requests: read the member, then write it back.
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith($"/member/{id}", handler.Requests[0].AbsolutePath);
        Assert.EndsWith($"/member/{id}", handler.Requests[1].AbsolutePath);
    }

    [Fact]
    public async Task DeleteMemberAsync_ServerReturns500ButMemberIsGone_ReportsSuccess()
    {
        // Regression for the alpha.8 finding member.delete.reports-500-on-success: Umbraco 17.x
        // returns an undeclared 500 from member delete even when the member IS removed. The client
        // must confirm the delete with a follow-up read and, finding the member gone (404), report
        // success rather than a false failure that breaks teardown scripts.
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Delete && Has(r, $"member/{id}"),
                HttpStatusCode.InternalServerError,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"member/{id}"),
                HttpStatusCode.NotFound,
                """{"status":404,"title":"Not Found"}"""
            );
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );

        var result = await client.DeleteMemberAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The delete was attempted and then verified with a read.
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith($"/member/{id}"));
    }

    [Fact]
    public async Task DeleteMemberAsync_ServerReturns500AndMemberStillExists_ReportsFailure()
    {
        // The 500 is only swallowed when the delete is proven to have worked. If the follow-up read
        // still returns the member, the 500 is a genuine failure and must be surfaced (status 500).
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Delete && Has(r, $"member/{id}"),
                HttpStatusCode.InternalServerError,
                ""
            )
            .When(
                r => r.Method == HttpMethod.Get && Has(r, $"member/{id}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{id}}","email":"m@example.com","variants":[{"name":"M"}]}"""
            );
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );

        var result = await client.DeleteMemberAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task UndeclaredServerError_500_SurfacesLegibleMessageNotKiotaNoErrorFactory()
    {
        // An HTTP 500 is not declared on most endpoints, so Kiota throws a bare ApiException whose
        // own message is "...no error factory is registered for this code: 500". That must be
        // rewritten into an actionable message while preserving the 500 status. (Surfaced by the
        // Umbraco 17.x member-delete server bug; see finding member.delete.500-when-membertype-present.)
        var (client, _) = ClientReturning("", HttpStatusCode.InternalServerError);

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.StatusCode);
        Assert.DoesNotContain("no error factory", result.ErrorMessage);
        Assert.Contains("internal error", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMemberAsync_HappyPath_CallsDeleteEndpoint()
    {
        // The ordinary success path: a clean delete reports success and hits DELETE /member/{id}.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteMemberAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/member/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    /// <summary>Substring match over a request's absolute URI (mirrors the content-write tests).</summary>
    private static bool Has(HttpRequestMessage r, string fragment) =>
        r.RequestUri!.AbsoluteUri.Contains(fragment);

    [Fact]
    public async Task GetLanguagesAsync_ParsesPagedShape()
    {
        // Regression for #41: GET /language returns a paged {total,items} object, not a
        // bare array, and must deserialize instead of throwing.
        var json = """
            {
              "total": 1,
              "items": [
                { "isoCode": "en-US", "name": "English", "isDefault": true, "isMandatory": false }
              ]
            }
            """;
        var (client, handler) = ClientReturning(json);

        var result = await client.GetLanguagesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            "/umbraco/management/api/v1/language",
            handler.LastRequestUri!.AbsolutePath
        );
        var lang = Assert.Single(result.Data!);
        Assert.Equal("en-US", lang.IsoCode);
        Assert.True(lang.IsDefault);
    }

    [Fact]
    public async Task GetContentAsync_ApiError_BecomesFailure()
    {
        // A non-2xx from a generated call is surfaced as a failed response (status + no
        // throw), preserving the "errors are data" contract through the Kiota guard.
        var (client, _) = ClientReturning("""{"title":"Not Found"}""", HttpStatusCode.NotFound);

        var result = await client.GetContentAsync(ct: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task CreateWebhookAsync_ReadBackEmpty_ReturnsTheRequestWithEventsAsAliases()
    {
        // Umbraco returns 201 Created with an empty body, and here the read-back returns nothing
        // either. The create still succeeded, so the request is returned with a non-empty id
        // (guards #74) - and each event as its alias, never in eventName (#295).
        // The create checks its events against GET webhook/events first (#234).
        var routes = Wire.Routed(
            ("/webhook/events", """{ "items": [ { "alias": "ContentPublished" } ] }""")
        );
        var client = Wire.Client(routes);

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest
            {
                Url = "https://example.com/hook",
                Events = ["ContentPublished"],
            },
            CancellationToken.None
        );

        var evt = Assert.Single(result.Data!.Events!);
        Assert.Equal(
            (true, false, "https://example.com/hook", "ContentPublished", (string?)null),
            (
                result.IsSuccess,
                result.Data.Id == Guid.Empty,
                result.Data.Url,
                evt.Alias,
                evt.EventName
            )
        );
    }

    [Fact]
    public async Task CreateWebhookAsync_ReadsTheWebhookBack_SoEventsHaveTheListShape()
    {
        // #295: create returned eventName = the alias; list returns the display name + alias.
        var id = Guid.NewGuid();
        var routes = Wire.Routed(
            ("/webhook/events", """{ "items": [ { "alias": "Umbraco.ContentPublish" } ] }"""),
            (
                $"/webhook/{id}",
                $$"""{ "id": "{{id}}", "url": "https://example.com/hook", "enabled": true, "events": [ { "eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish" } ] }"""
            )
        );

        var result = await Wire.Client(routes)
            .CreateWebhookAsync(
                new CreateWebhookRequest
                {
                    Id = id,
                    Url = "https://example.com/hook",
                    Events = ["Umbraco.ContentPublish"],
                },
                CancellationToken.None
            );

        Assert.Equal(
            new WebhookEvent
            {
                EventName = "Content Published",
                EventType = "Content",
                Alias = "Umbraco.ContentPublish",
            },
            Assert.Single(result.Data!.Events!)
        );
    }

    [Fact]
    public async Task Error_WithValidationErrors_SurfacesFieldNames()
    {
        // Regression for #48: a 400 ProblemDetails must surface the offending field, not just
        // the generic title. The webhook create is on the generated-client path, so this also
        // proves FormatProblemDetailsErrors flattens the AdditionalData "errors" map at parity
        // with the hand-written BuildErrorAsync.
        var json = """
            {
              "title": "One or more validation errors occurred.",
              "errors": { "$.icon": ["The Icon field is required."] }
            }
            """;
        var (client, _) = ClientReturning(json, HttpStatusCode.BadRequest);

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest { Url = "https://example.com/hook" },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("$.icon", result.ErrorMessage);
        Assert.Contains("Icon field is required", result.ErrorMessage);
    }

    [Fact]
    public async Task GetWebhooksAsync_ParsesEventsAsObjects()
    {
        // Regression for #46: the API returns events as objects, not strings, which used
        // to throw "cannot convert to System.String" once any webhook existed.
        var json = """
            {
              "total": 1,
              "items": [
                {
                  "id": "33333333-3333-3333-3333-333333333333",
                  "url": "https://example.com/hook",
                  "enabled": true,
                  "events": [
                    { "eventName": "ContentPublished", "eventType": "Other", "alias": "ContentPublished" }
                  ]
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetWebhooksAsync(0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var webhook = Assert.Single(result.Data!.Items);
        var evt = Assert.Single(webhook.Events!);
        Assert.Equal("ContentPublished", evt.EventName);
    }

    [Fact]
    public async Task GetDictionaryItemByIdAsync_AfterResolvingAHumanKey_ReadsTheResolvedId()
    {
        // Regression for #44: a human key must be resolved to the item id (the endpoint is
        // keyed by GUID) rather than 404ing. The stub returns a list containing the key, so
        // the by-id GET should target the resolved id. Resolution is the caller's (#262), so
        // this composes the two the way `dictionary get` does.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"total":1,"items":[{"id":"{{id}}","name":"Admin"}]}"""
        );

        var result = await client.WithResolvedAsync(
            EntityKind.DictionaryItem,
            "Admin",
            resolved => client.GetDictionaryItemByIdAsync(resolved, CancellationToken.None)
        );

        Assert.True(result.IsSuccess);
        // The by-id read is followed by the parent lookup (#290), so it is not the last request.
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith($"/dictionary/{id}"));
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownDictionaryKey_IsInvalidArgument()
    {
        // Regression for #44: an unmatched key yields a clear 404, not a silent empty item.
        var (client, _) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.ResolveIdAsync(
            EntityKind.DictionaryItem,
            "Nope",
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }

    [Fact]
    public void CreateDocumentTypeRequest_IncludesApiRequiredFields()
    {
        // Regression for #47: the payload must carry icon, the varies-by flags, the
        // cleanup object and the allowed-* collections, or Umbraco 400s the create.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new CreateDocumentTypeRequest { Name = "Widget", Alias = "widget" }
        );

        Assert.Contains("\"icon\":\"icon-document\"", json);
        Assert.Contains("\"variesByCulture\":false", json);
        Assert.Contains("\"cleanup\":", json);
        Assert.Contains("\"allowedTemplates\":", json);
    }

    [Fact]
    public async Task GetContentByIdAsync_FlattensVariantNameAndDates()
    {
        // Regression for #42: single-item GET carries name/dates under variants[], which
        // must be surfaced instead of an empty name and 0001-01-01 dates.
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "documentType": { "id": "22222222-2222-2222-2222-222222222222" },
              "variants": [
                {
                  "culture": null,
                  "name": "Home",
                  "state": "Published",
                  "createDate": "2020-01-02T03:04:05+00:00",
                  "updateDate": "2021-02-03T04:05:06+00:00"
                }
              ]
            }
            """;
        var (client, _) = ClientReturning(json);

        var result = await client.GetContentByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Home", result.Data!.Name);
        Assert.True(result.Data.IsPublished);
        Assert.Equal(2020, result.Data.CreateDate.Year);
    }

    [Fact]
    public async Task KiotaError_ProblemDetails_SurfacesDetailNotRawException()
    {
        // Regression for #48: a ProblemDetails error on the generated-client path must
        // surface its detail/title, not "Exception of type '...ProblemDetails' was thrown."
        var json = """
            {
              "type": "Error",
              "title": "Not Found",
              "status": 404,
              "detail": "The document could not be found"
            }
            """;
        var (client, _) = ClientReturning(json, HttpStatusCode.NotFound);

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.DoesNotContain("Exception of type", result.ErrorMessage);
        Assert.Contains("could not be found", result.ErrorMessage);
    }

    [Fact]
    public async Task InviteUserAsync_201EmptyBody_IsSuccess()
    {
        // Invite is a void POST on the generated client: Umbraco sends the email and returns
        // 201 with no body, which must map to an empty success (not a deserialization error).
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.InviteUserAsync(
            new InviteUserRequest { Email = "new.user@example.com", Name = "New User" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FormatProblemDetailsErrors_FlattensFieldErrorsFromAdditionalData()
    {
        // #48 parity on the Kiota path: the generated ProblemDetails has no typed "errors"
        // property, so the field-level map arrives as an UntypedNode under AdditionalData.
        // FormatProblemDetailsErrors must flatten it to "field: message" like the
        // hand-written FormatValidationErrors does.
        var pd = new Umbraco.Cli.Client.Generated.Models.ProblemDetails
        {
            Title = "One or more validation errors occurred.",
            Status = 400,
        };
        pd.AdditionalData["errors"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedObject(
            new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
            {
                ["isoCode"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedArray(
                    new List<Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
                    {
                        new Microsoft.Kiota.Abstractions.Serialization.UntypedString(
                            "The IsoCode field is required."
                        ),
                    }
                ),
            }
        );

        var formatted = UmbracoManagementClient.FormatProblemDetailsErrors(pd);

        Assert.NotNull(formatted);
        Assert.Contains("isoCode", formatted);
        Assert.Contains("IsoCode field is required", formatted);
    }

    [Fact]
    public void FormatProblemDetailsErrors_FlattensArrayOfObjectsForm()
    {
        // #48 parity: Umbraco can also return the errors map as an array of objects
        // ([{"field":["msg"]}]), which the hand-written FormatValidationErrors handles - the
        // Kiota path must too, or the field detail is lost on that shape.
        var pd = new Umbraco.Cli.Client.Generated.Models.ProblemDetails { Status = 400 };
        pd.AdditionalData["errors"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedArray(
            new List<Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
            {
                new Microsoft.Kiota.Abstractions.Serialization.UntypedObject(
                    new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
                    {
                        ["url"] = new Microsoft.Kiota.Abstractions.Serialization.UntypedArray(
                            new List<Microsoft.Kiota.Abstractions.Serialization.UntypedNode>
                            {
                                new Microsoft.Kiota.Abstractions.Serialization.UntypedString(
                                    "The Url field is required."
                                ),
                            }
                        ),
                    }
                ),
            }
        );

        var formatted = UmbracoManagementClient.FormatProblemDetailsErrors(pd);

        Assert.NotNull(formatted);
        Assert.Contains("url", formatted);
        Assert.Contains("Url field is required", formatted);
    }

    [Fact]
    public async Task CreateMemberAsync_ResolvesTypeAliasAndEchoesRequest()
    {
        // #79: member create resolves the member-type alias to an id (tree walk then GET-by-id
        // alias match - the item search indexes names, not aliases), then POSTs with a
        // client-supplied id and echoes the accepted request (the create response is empty). The
        // POST body carries the resolved member-type id and the name as a variant.
        var memberTypeId = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r =>
                    r.Method == HttpMethod.Get
                    && r.RequestUri!.AbsoluteUri.Contains("tree/member-type/root"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{memberTypeId}}","name":"Member","isFolder":false}]}"""
            )
            .When(
                r =>
                    r.Method == HttpMethod.Get
                    && r.RequestUri!.AbsoluteUri.Contains($"member-type/{memberTypeId}"),
                HttpStatusCode.OK,
                $$"""{"id":"{{memberTypeId}}","alias":"member","name":"Member"}"""
            )
            .When(
                r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/member"),
                HttpStatusCode.Created,
                ""
            );
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );

        var result = await client.CreateMemberAsync(
            new CreateMemberRequest
            {
                Email = "m@example.com",
                Name = "M",
                MemberType = new ContentTypeReference { Alias = "member" },
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("m@example.com", result.Data.Email);
        Assert.Equal("M", result.Data.Name);
        var postBody = handler.RawBodyOf(HttpMethod.Post, "/member");
        Assert.Contains(memberTypeId.ToString(), postBody);
        Assert.Contains("\"M\"", postBody); // name carried as a variant
    }

    [Fact]
    public async Task CreateMemberAsync_UnknownTypeAlias_IsInvalidArgument()
    {
        // An unresolvable member-type alias yields a clean 404 and never POSTs a member.
        var handler = new RoutingHandler().When(
            r => r.Method == HttpMethod.Get,
            HttpStatusCode.OK,
            """{"total":0,"items":[]}"""
        );
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );

        var result = await client.CreateMemberAsync(
            new CreateMemberRequest
            {
                Email = "m@example.com",
                Name = "M",
                MemberType = new ContentTypeReference { Alias = "nope" },
            },
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.DoesNotContain(handler.Requests, u => u.AbsolutePath.EndsWith("/member"));
    }

    [Fact]
    public void FormatProblemDetailsErrors_NoErrorsMap_ReturnsNull()
    {
        // With no "errors" entry, there is nothing to append and the base detail/title
        // message stands alone.
        var pd = new Umbraco.Cli.Client.Generated.Models.ProblemDetails { Title = "Nope" };

        Assert.Null(UmbracoManagementClient.FormatProblemDetailsErrors(pd));
    }

    [Fact]
    public async Task Error_EmptyBody_FallsBackToReasonPhrase()
    {
        // Regression for #48: a bare 404 (empty body) must not produce a blank message.
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.GetContentByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
}
