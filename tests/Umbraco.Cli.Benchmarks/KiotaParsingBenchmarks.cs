using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Microsoft.Kiota.Serialization.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// Reading large Management API responses: the Kiota JSON parse into the generated models that
/// every typed call pays, and the full client path of a raw read (request adapter, error mapping,
/// DOM parse) over a stub HTTP handler, so no server is involved.
/// </summary>
[MemoryDiagnoser]
public class KiotaParsingBenchmarks
{
    private const string Json = "application/json";

    /// <summary>How many items the tree page holds.</summary>
    private const int TreeItems = 1000;

    /// <summary>How many text values the large document carries on top of its standard eight.</summary>
    private const int DocumentValues = 500;

    private readonly JsonParseNodeFactory _parser = new();
    private byte[] _treePage = null!;
    private byte[] _document = null!;
    private HttpClient _http = null!;
    private UmbracoManagementClient _client = null!;

    /// <summary>Serialises the response bodies and wires the client to a handler that returns them.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _treePage = Fixtures.DocumentTreePage(TreeItems);
        _document = Fixtures.LargeDocument(DocumentValues);
        _http = new HttpClient(new CannedResponseHandler(_document))
        {
            BaseAddress = new Uri("https://umbraco.invalid/"),
        };
        _client = new UmbracoManagementClient(_http);
    }

    /// <summary>Disposes the stub HTTP client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _http.Dispose();

    /// <summary>A <c>tree/document/children</c> page into the generated paged tree model.</summary>
    /// <returns>The parsed page.</returns>
    [Benchmark]
    public async Task<PagedDocumentTreeItemResponseModel?> ParseDocumentTreePage()
    {
        using var stream = new MemoryStream(_treePage, writable: false);
        var node = await _parser.GetRootParseNodeAsync(Json, stream);
        return node.GetObjectValue(PagedDocumentTreeItemResponseModel.CreateFromDiscriminatorValue);
    }

    /// <summary>
    /// A <c>document/{id}</c> body into the generated document model, whose values are untyped
    /// nodes: the costlier shape, since every value is walked into a node tree.
    /// </summary>
    /// <returns>The parsed document.</returns>
    [Benchmark]
    public async Task<DocumentResponseModel?> ParseLargeDocument()
    {
        using var stream = new MemoryStream(_document, writable: false);
        var node = await _parser.GetRootParseNodeAsync(Json, stream);
        return node.GetObjectValue(DocumentResponseModel.CreateFromDiscriminatorValue);
    }

    /// <summary>
    /// <see cref="UmbracoManagementClient.GetDocumentRawAsync"/> end to end, as <c>content export</c>
    /// reads each document: the request adapter, the ProblemDetails error mapping and the parse
    /// into a JSON DOM.
    /// </summary>
    /// <returns>The document body.</returns>
    /// <exception cref="InvalidOperationException">The client reported a failure.</exception>
    [Benchmark]
    public async Task<JsonNode> ClientGetDocumentRaw()
    {
        var response = await _client.GetDocumentRawAsync(Guid.Empty);
        return response.Data
            ?? throw new InvalidOperationException($"The read failed: {response.ErrorMessage}");
    }

    /// <summary>Answers every request with the same 200 JSON body, standing in for the server.</summary>
    /// <param name="body">The response body.</param>
    private sealed class CannedResponseHandler(byte[] body) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(Json);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                    RequestMessage = request,
                }
            );
        }
    }
}
