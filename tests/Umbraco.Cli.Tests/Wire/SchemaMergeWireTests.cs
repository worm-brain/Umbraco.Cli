using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The write half of the schema round-trip (#201): a body of the raw GET's shape is merged into the
/// item before the PUT, so keys the caller leaves out survive. Before this, <c>update</c> PUT the
/// body verbatim and a partial body wiped every property's validation, the list view and the
/// allowed child types.
/// </summary>
public class SchemaMergeWireTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private const string Current = """
        { "id": "__ID__", "alias": "blog", "name": "Blog",
          "collection": { "id": "c0ffee00-0000-0000-0000-000000000000" },
          "allowedDocumentTypes": [ { "documentType": { "id": "b1000000-0000-0000-0000-000000000000" } } ],
          "properties": [ { "alias": "title", "validation": { "mandatory": true } } ] }
        """;

    /// <summary>A handler that answers the item GET with <see cref="Current"/> and accepts the PUT.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Handler() =>
        new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                Current.Replace("__ID__", Id.ToString())
            )
            .When(_ => true, HttpStatusCode.OK, "");

    /// <summary>Merges <paramref name="patch"/> into the document type and returns the PUT body.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="patch">The body to merge.</param>
    /// <param name="replace">Whether to replace instead.</param>
    /// <returns>The PUT body.</returns>
    private static async Task<JsonObject> PutBody(
        RoutingHandler handler,
        string patch,
        WriteMode mode = WriteMode.Merge
    )
    {
        var result = await Wire.Client(handler)
            .MergeSchemaItemAsync(
                EntityKind.DocumentType,
                Id,
                JsonNode.Parse(patch)!,
                mode,
                CancellationToken.None
            );
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return handler.BodyOf(HttpMethod.Put, $"/document-type/{Id}");
    }

    [Fact]
    public async Task MergeSchemaItemAsync_PartialBody_KeepsTheKeysItLeavesOut()
    {
        var body = await PutBody(Handler(), """{ "name": "Journal" }""");

        Assert.Equal("Journal", body["name"]!.GetValue<string>());
        Assert.NotNull(body["collection"]);
        Assert.True(body["properties"]![0]!["validation"]!["mandatory"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MergeSchemaItemAsync_ArrayInTheBody_ReplacesTheWholeArray()
    {
        var body = await PutBody(Handler(), """{ "properties": [ { "alias": "summary" } ] }""");

        var aliases = body["properties"]!
            .AsArray()
            .Select(p => p!["alias"]!.GetValue<string>())
            .ToList();
        Assert.Equal(["summary"], aliases);
    }

    [Fact]
    public async Task MergeSchemaItemAsync_BodyFromAnotherItem_KeepsTheTargetsId()
    {
        var body = await PutBody(
            Handler(),
            $$"""{ "id": "{{Guid.NewGuid()}}", "name": "Journal" }"""
        );

        Assert.Equal(Id.ToString(), body["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task MergeSchemaItemAsync_Replace_SendsTheBodyWithoutReadingTheItem()
    {
        var handler = Handler();

        var body = await PutBody(handler, """{ "name": "Journal" }""", WriteMode.Replace);

        Assert.False(body.ContainsKey("collection"));
        handler.AssertNoRequest(HttpMethod.Get, $"/document-type/{Id}");
    }

    [Fact]
    public async Task MergeSchemaItemAsync_BodyIsNotAnObject_FailsWithoutWriting()
    {
        var handler = Handler();

        var result = await Wire.Client(handler)
            .MergeSchemaItemAsync(
                EntityKind.DocumentType,
                Id,
                JsonNode.Parse("[1]")!,
                ct: CancellationToken.None
            );

        // #280: the caller's input, refused before sending, so invalid_argument rather than a 400.
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        handler.AssertNoRequest(HttpMethod.Put, $"/document-type/{Id}");
    }

    [Theory]
    [InlineData(EntityKind.DocumentType, "document-type")]
    [InlineData(EntityKind.MediaType, "media-type")]
    [InlineData(EntityKind.MemberType, "member-type")]
    [InlineData(EntityKind.DataType, "data-type")]
    [InlineData(EntityKind.Template, "template")]
    public async Task MergeSchemaItemAsync_EachKind_WritesToItsOwnEndpoint(
        EntityKind kind,
        string segment
    )
    {
        var handler = Handler();

        await Wire.Client(handler)
            .MergeSchemaItemAsync(
                kind,
                Id,
                JsonNode.Parse("""{ "name": "X" }""")!,
                ct: CancellationToken.None
            );

        handler.AssertRequested(HttpMethod.Put, $"/{segment}/{Id}");
    }
}
