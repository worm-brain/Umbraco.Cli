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

    [Fact]
    public async Task CreateWebhookAsync_WithNameAndDescription_EchoesAndSendsThem()
    {
        // #80: name/description are sent in the request body and echoed on the response (the
        // 201 has an empty body, so the echo is what the command reports).
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

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
        Assert.Contains("My Hook", handler.RequestBodies[0]!); // sent, not just echoed
    }

    [Fact]
    public async Task CreateWebhookAsync_WithSuppliedId_UsesThatId()
    {
        // #86: a caller-supplied id enables idempotent creates — it must be used verbatim
        // (and appear in the request body) rather than a fresh GUID being generated.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);

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
        Assert.Contains(id.ToString(), handler.RequestBodies[0]!);
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

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Data!.Id);
    }

    [Fact]
    public async Task CreateMediaTypeAsync_201EmptyBody_EchoesRequestWithGeneratedId()
    {
        // #55: like the other migrated creates, the client supplies the id up front and echoes
        // the accepted request, so a 201 with an empty body reports success with a non-empty id
        // and the alias populated (guards #74).
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateMediaTypeAsync(
            new CreateMediaTypeRequest { Name = "Custom Image", Alias = "customImage" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("customImage", result.Data.Alias);
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

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/document/{id}/copy", handler.LastRequestUri!.AbsolutePath);
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

        var result = await client.RestoreMediaAsync(id, ct: CancellationToken.None);

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
        // item (a media type id is passed, so no resolution request is made). The stub returns
        // 201/empty for both; the client echoes the client-generated media id.
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
        // Two requests, in order: stage the file, then create the media item.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("temporary-file", handler.Requests[0].AbsoluteUri);
        Assert.EndsWith("/umbraco/management/api/v1/media", handler.Requests[1].AbsolutePath);
    }

    [Fact]
    public async Task UploadMediaAsync_ResolvesMediaTypeByName()
    {
        // #57: a non-GUID --media-type is resolved via the media-type item search endpoint,
        // matching on name. The first request must be that search.
        var mediaTypeId = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"total":1,"items":[{"id":"{{mediaTypeId}}","name":"Image"}]}"""
        );
        using var stream = new MemoryStream(new byte[] { 1 });

        var result = await client.UploadMediaAsync(
            Guid.Empty,
            "Photo",
            stream,
            "photo.jpg",
            "image/jpeg",
            "Image",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("item/media-type/search", handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task UploadMediaAsync_UnknownMediaTypeName_Returns404()
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
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
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
    public async Task CreateMemberTypeAsync_201EmptyBody_EchoesRequestWithGeneratedId()
    {
        // #56: the client supplies the id up front and echoes the accepted request, so a 201
        // with an empty body reports success with a non-empty id and the alias populated.
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateMemberTypeAsync(
            new CreateMemberTypeRequest { Name = "Author", Alias = "author" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("author", result.Data.Alias);
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
    public async Task CreateDataTypeAsync_201EmptyBody_EchoesRequestWithGeneratedId()
    {
        // #59: data-type create supplies a client id and echoes the request on the empty 201.
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

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("Umbraco.TextBox", result.Data.EditorAlias);
    }

    [Fact]
    public async Task DeleteDictionaryItemAsync_CallsDeleteEndpoint()
    {
        // #59: dictionary delete targets DELETE /dictionary/{id}.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteDictionaryItemAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/dictionary/{id}", handler.LastRequestUri!.AbsolutePath);
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
    public async Task UpdateTemplateAsync_MissingTemplate_Returns404()
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
    public async Task GetMediaTypeByIdAsync_CallsByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"id":"{{id}}","name":"Image","alias":"image"}"""
        );

        var result = await client.GetMediaTypeByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image", result.Data!.Alias);
        Assert.EndsWith($"/media-type/{id}", handler.LastRequestUri!.AbsolutePath);
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
    public async Task GetMemberTypeByIdAsync_CallsByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"id":"{{id}}","name":"Author","alias":"author"}"""
        );

        var result = await client.GetMemberTypeByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("author", result.Data!.Alias);
        Assert.EndsWith($"/member-type/{id}", handler.LastRequestUri!.AbsolutePath);
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
    public async Task UploadMediaAsync_EmptyCreateBody_EchoesClientGeneratedIdAndName()
    {
        // #79: on the generated client the media create is a void POST (empty 201, and Kiota does
        // not surface the Location header), so the returned id is the client-generated one and the
        // name is echoed - the returned payload reflects the accepted request, not a re-read.
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
            CancellationToken.None
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
    public async Task CreateWebhookAsync_201EmptyBody_EchoesRequestWithGeneratedId()
    {
        // Umbraco returns 201 Created with an empty body. On the generated-client path the
        // client supplies the id up front (Umbraco 14+ accepts a client GUID) and echoes the
        // accepted request, so the create reports success with a non-empty id and the request
        // fields populated instead of a blank payload (guards #74).
        var (client, _) = ClientReturning("", HttpStatusCode.Created);

        var result = await client.CreateWebhookAsync(
            new CreateWebhookRequest
            {
                Url = "https://example.com/hook",
                Events = ["ContentPublished"],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.Id);
        Assert.Equal("https://example.com/hook", result.Data.Url);
        var evt = Assert.Single(result.Data.Events!);
        Assert.Equal("ContentPublished", evt.EventName);
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
    public async Task GetDictionaryItemByKeyAsync_ResolvesHumanKeyToId()
    {
        // Regression for #44: a human key must be resolved to the item id (the endpoint is
        // keyed by GUID) rather than 404ing. The stub returns a list containing the key, so
        // the by-id GET should target the resolved id.
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"total":1,"items":[{"id":"{{id}}","name":"Admin"}]}"""
        );

        var result = await client.GetDictionaryItemByKeyAsync("Admin", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith($"/dictionary/{id}", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetDictionaryItemByKeyAsync_UnknownKey_Returns404()
    {
        // Regression for #44: an unmatched key yields a clear 404, not a silent empty item.
        var (client, _) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetDictionaryItemByKeyAsync("Nope", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
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
    public async Task CreateMemberAsync_201EmptyBody_IsSuccessWithIdFromLocation()
    {
        // #43 guard for the creates still on the hand-written path (content/member): a 201
        // with an empty body must succeed and surface the new id parsed from the Location
        // header. (Migrated creates supply a client GUID instead; this keeps the Location
        // path covered until #79 finishes the migration - the migrated webhook create used to
        // be this test's subject.)
        var id = Guid.NewGuid();
        var (client, _) = ClientReturning(
            "",
            HttpStatusCode.Created,
            location: $"/umbraco/management/api/v1/member/{id}"
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
        Assert.Equal(id, result.Data!.Id);
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
