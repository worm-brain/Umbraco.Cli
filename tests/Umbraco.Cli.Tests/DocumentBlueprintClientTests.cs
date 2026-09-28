using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the document-blueprint client methods (#113): each verb hits the right endpoint and
/// HTTP method, create/update send the value payload, get/scaffold read the raw JSON, and create
/// best-effort hydrates via a follow-up read. Uses a handler that records the full request sequence
/// (create does POST then a hydration GET), over the real client.
/// </summary>
public class DocumentBlueprintClientTests
{
    private sealed record Recorded(HttpMethod Method, Uri Uri, string? Body);

    private sealed class RecordingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Recorded(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        public Recorded First(HttpMethod method) => Requests.First(r => r.Method == method);
    }

    private static (UmbracoManagementClient Client, RecordingHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        var handler = new RecordingHandler(json, status);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    private const string BlueprintJson = """{"id":"0f0f0f0f-0000-0000-0000-000000000000"}""";

    [Fact]
    public async Task CreateDocumentBlueprintAsync_PostsValuesThenHydrates()
    {
        var (client, handler) = ClientReturning(BlueprintJson);
        var id = Guid.NewGuid();
        var docType = Guid.NewGuid();

        var result = await client.CreateDocumentBlueprintAsync(
            new CreateDocumentBlueprintRequest
            {
                Id = id,
                DocumentType = new ContentTypeReference { Id = docType }, // id -> no alias resolve
                Values = [new ContentValue { Alias = "title", Value = "Hello" }],
                Variants = [new ContentVariant { Name = "Starter" }],
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var post = handler.First(HttpMethod.Post);
        Assert.EndsWith("/document-blueprint", post.Uri.AbsolutePath);
        Assert.Contains("title", post.Body);
        Assert.Contains("Starter", post.Body);
        // Best-effort hydration re-reads the item by id.
        Assert.Contains(
            handler.Requests,
            r =>
                r.Method == HttpMethod.Get && r.Uri.AbsoluteUri.Contains($"document-blueprint/{id}")
        );
    }

    [Fact]
    public async Task CreateFromDocumentAsync_PostsToFromDocumentEndpoint()
    {
        var (client, handler) = ClientReturning(BlueprintJson);
        var source = Guid.NewGuid();

        var result = await client.CreateDocumentBlueprintFromDocumentAsync(
            new CreateBlueprintFromDocumentRequest { Document = source, Name = "Starter" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var post = handler.First(HttpMethod.Post);
        Assert.EndsWith("/document-blueprint/from-document", post.Uri.AbsolutePath);
        Assert.Contains(source.ToString(), post.Body);
    }

    [Fact]
    public async Task GetDocumentBlueprintAsync_ReadsRawByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"id":"x","values":[{"alias":"title"}]}""");

        var result = await client.GetDocumentBlueprintAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var get = handler.First(HttpMethod.Get);
        Assert.EndsWith($"/document-blueprint/{id}", get.Uri.AbsolutePath);
        Assert.Equal("title", result.Data!["values"]![0]!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task ScaffoldDocumentBlueprintAsync_ReadsScaffoldEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"id":"x"}""");

        var result = await client.ScaffoldDocumentBlueprintAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            $"/document-blueprint/{id}/scaffold",
            handler.First(HttpMethod.Get).Uri.AbsolutePath
        );
    }

    [Fact]
    public async Task ScaffoldDocumentBlueprintAsync_LeavesOutTheBlueprintsId()
    {
        // #299: the scaffold is a body for a new document, so it must not carry the blueprint's id.
        var (client, _) = ClientReturning(
            """{"id":"4b1e0c6a-0000-0000-0000-000000000001","documentType":{"id":"4b1e0c6a-0000-0000-0000-000000000002"},"values":[],"variants":[]}"""
        );

        var result = await client.ScaffoldDocumentBlueprintAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        Assert.Null(result.Data?["id"]);
    }

    /// <summary>A blueprint with a title and a featured image, as Umbraco returns it.</summary>
    private static string ExistingBlueprint(Guid id) =>
        $$"""
            {"id":"{{id}}","documentType":{"id":"{{Guid.NewGuid()}}"},
             "values":[{"alias":"title","culture":null,"segment":null,"value":"Old"},
                       {"alias":"featuredImage","culture":null,"segment":null,"value":"img"}],
             "variants":[{"culture":null,"segment":null,"name":"Post"}]}
            """;

    [Fact]
    public async Task UpdateDocumentBlueprintAsync_MergesIntoTheBlueprint()
    {
        // #242: the PUT replaced the body, so fields the caller left out (featuredImage) vanished.
        var id = Guid.NewGuid();
        var handler = Wire.Existing(ExistingBlueprint(id));

        var result = await Wire.Client(handler)
            .UpdateDocumentBlueprintAsync(
                id,
                new UpdateDocumentBlueprintRequest
                {
                    Values = [new ContentValue { Alias = "title", Value = "New" }],
                },
                ct: CancellationToken.None
            );

        Assert.True(result.IsSuccess);
        var values = handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{id}")[
            "values"
        ]!.AsArray();
        Assert.Equal(
            ["title=New", "featuredImage=img"],
            values.Select(v => $"{v!["alias"]}={v["value"]}")
        );
    }

    [Fact]
    public async Task UpdateDocumentBlueprintAsync_Replace_SendsOnlyTheGivenValues()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Existing(ExistingBlueprint(id));

        await Wire.Client(handler)
            .UpdateDocumentBlueprintAsync(
                id,
                new UpdateDocumentBlueprintRequest
                {
                    Values = [new ContentValue { Alias = "title", Value = "New" }],
                },
                WriteMode.Replace,
                ct: CancellationToken.None
            );

        var values = handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{id}")[
            "values"
        ]!.AsArray();
        Assert.Equal("title", Assert.Single(values)!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteDocumentBlueprintAsync_DeletesByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.DeleteDocumentBlueprintAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            $"/document-blueprint/{id}",
            handler.First(HttpMethod.Delete).Uri.AbsolutePath
        );
    }

    [Fact]
    public async Task MoveDocumentBlueprintAsync_PutsTargetToMoveEndpoint()
    {
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.MoveDocumentBlueprintAsync(id, target, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var put = handler.First(HttpMethod.Put);
        Assert.EndsWith($"/document-blueprint/{id}/move", put.Uri.AbsolutePath);
        Assert.Contains(target.ToString(), put.Body);
    }

    [Fact]
    public async Task GetDocumentBlueprintsAsync_Root_ReadsTreeRoot()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","name":"Starter","isFolder":false,"hasChildren":false}]}"""
        );

        var result = await client.GetDocumentBlueprintsAsync(null, 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            "/tree/document-blueprint/root",
            handler.First(HttpMethod.Get).Uri.AbsolutePath
        );
        Assert.Equal("Starter", Assert.Single(result.Data!.Items).Name);
    }

    [Fact]
    public async Task GetDocumentBlueprintsAsync_WithParent_ReadsTreeChildrenWithParentId()
    {
        var parent = Guid.NewGuid();
        var (client, handler) = ClientReturning("""{"total":0,"items":[]}""");

        var result = await client.GetDocumentBlueprintsAsync(
            parent,
            0,
            100,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var get = handler.First(HttpMethod.Get);
        Assert.EndsWith("/tree/document-blueprint/children", get.Uri.AbsolutePath);
        Assert.Contains($"parentId={parent}", get.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task CreateBlueprintFolderAsync_PostsToFolderEndpointAndEchoesId()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);
        var id = Guid.NewGuid();

        var result = await client.CreateBlueprintFolderAsync(
            new CreateBlueprintFolderRequest { Id = id, Name = "Marketing" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var post = handler.First(HttpMethod.Post);
        Assert.EndsWith("/document-blueprint/folder", post.Uri.AbsolutePath);
        Assert.Contains("Marketing", post.Body);
        Assert.Equal(id, result.Data!.Id);
    }

    [Fact]
    public async Task DeleteBlueprintFolderAsync_DeletesFolderByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("");

        var result = await client.DeleteBlueprintFolderAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(
            $"/document-blueprint/folder/{id}",
            handler.First(HttpMethod.Delete).Uri.AbsolutePath
        );
    }
}
