using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Authoring a document type or a data type directly (#161/#169).
/// <para>
/// Neither could express what actually matters about them - a document type's properties and
/// groups, a data type's editor configuration - so the only way to author either was
/// <c>schema export -&gt; jq -&gt; schema apply</c> through the entire instance. These paths take
/// the Management API body itself, so what a caller edits is what the API defines.
/// </para>
/// </summary>
public class SchemaAuthoringTests
{
    [Fact]
    public async Task CreateSchemaRawDocumentType_SendsTheBodyVerbatim()
    {
        var handler = Wire.Blank();
        var body = JsonNode.Parse(
            """
            {
              "alias": "blogPost",
              "name": "Blog Post",
              "properties": [ { "alias": "title", "name": "Title", "dataType": { "id": "11111111-1111-1111-1111-111111111111" } } ],
              "containers": [ { "name": "Content", "type": "Group" } ],
              "variesByCulture": true
            }
            """
        )!;

        var result = await Wire.Client(handler)
            .CreateSchemaRawAsync(EntityKind.DocumentType, body, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // Verbatim: nothing between the caller's body and the API, which is the point - a typed
        // record in the middle is what dropped properties and groups in the first place.
        var sent = handler.BodyOf(HttpMethod.Post, "/document-type");
        Assert.Single(sent["properties"]!.AsArray());
        Assert.Single(sent["containers"]!.AsArray());
        Assert.True(sent["variesByCulture"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateByReferenceDataType_CanSetTheEditorConfiguration()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = Wire.Blank();
        var body = JsonNode.Parse(
            """
            {
              "name": "Blog Categories",
              "editorAlias": "Umbraco.DropDown.Flexible",
              "editorUiAlias": "Umb.PropertyEditorUi.Dropdown",
              "values": [ { "alias": "items", "value": ["News", "Opinion"] } ]
            }
            """
        )!;

        await Wire.Client(handler)
            .MergeSchemaItemAsync(
                EntityKind.DataType,
                id,
                body,
                WriteMode.Replace,
                CancellationToken.None
            );

        // #169: `values` is the dropdown's items. The typed update deliberately never exposed it,
        // so setting it needed a schema round-trip.
        var sent = handler.BodyOf(HttpMethod.Put, $"/data-type/{id}");
        var items = Assert.Single(sent["values"]!.AsArray());
        Assert.Equal(
            ["News", "Opinion"],
            items!["value"]!.AsArray().Select(v => v!.GetValue<string>())
        );
    }

    // ── addressing a type by the key a human has (#159), resolved by the client ──

    [Fact]
    public async Task UpdateByReferenceDocumentType_ByAlias_ResolvesItThenPutsToTheResolvedId()
    {
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var handler = Wire.Routed(
            ("tree/document-type", $$"""{ "total": 1, "items": [ { "id": "{{id}}" } ] }"""),
            ($"document-type/{id}", $$"""{ "id": "{{id}}", "alias": "blogPost" }""")
        );

        var result = await Wire.Client(handler)
            .UpdateByReferenceAsync(
                EntityKind.DocumentType,
                "blogPost",
                JsonNode.Parse("""{ "alias": "blogPost", "name": "Blog Post" }""")!,
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // The PUT has to land on the resolved id - the alias is not a valid path segment.
        Assert.Equal(
            "Blog Post",
            handler.BodyOf(HttpMethod.Put, $"/document-type/{id}")["name"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UpdateByReferenceDocumentType_UnknownAlias_FailsWithoutWriting()
    {
        var handler = Wire.Routed(("tree/document-type", """{ "total": 0, "items": [] }"""));

        var result = await Wire.Client(handler)
            .UpdateByReferenceAsync(
                EntityKind.DocumentType,
                "noSuchType",
                JsonNode.Parse("""{ "alias": "noSuchType" }""")!,
                CancellationToken.None
            );

        // The resolve failure is this call's failure - reporting anything else would have the
        // caller looking for a document type that was never touched.
        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.Contains("noSuchType", result.ErrorMessage);
        handler.AssertNoRequest(HttpMethod.Put, "/document-type");
    }

    [Fact]
    public async Task UpdateByReferenceDataType_ByName_ResolvesItThenPutsToTheResolvedId()
    {
        var id = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var handler = Wire.Routed(
            (
                "item/data-type/search",
                $$"""{ "total": 1, "items": [ { "id": "{{id}}", "name": "Blog Categories" } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .UpdateByReferenceAsync(
                EntityKind.DataType,
                "Blog Categories",
                JsonNode.Parse("""{ "name": "Blog Categories" }""")!,
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        handler.AssertRequested(HttpMethod.Put, $"/data-type/{id}");
    }

    [Fact]
    public async Task UpdateDataTypeAsync_ByName_ResolvesItThenPutsToTheResolvedId()
    {
        var id = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var handler = Wire.Routed(
            (
                "item/data-type/search",
                $$"""{ "total": 1, "items": [ { "id": "{{id}}", "name": "Textstring" } ] }"""
            ),
            (
                $"data-type/{id}",
                $$"""{ "id": "{{id}}", "name": "Textstring", "editorAlias": "Umbraco.TextBox", "editorUiAlias": "Umb.PropertyEditorUi.TextBox" }"""
            )
        );

        var result = await Wire.Client(handler)
            .UpdateDataTypeAsync(
                "Textstring",
                new UpdateDataTypeRequest { Name = "Short Text" },
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            "Short Text",
            handler.BodyOf(HttpMethod.Put, $"/data-type/{id}")["name"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UpdateDataTypeAsync_UnknownName_FailsWithoutWriting()
    {
        var handler = Wire.Routed(("item/data-type/search", """{ "total": 0, "items": [] }"""));

        var result = await Wire.Client(handler)
            .UpdateDataTypeAsync(
                "NotARealDataType",
                new UpdateDataTypeRequest { Name = "Short Text" },
                CancellationToken.None
            );

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.Contains("NotARealDataType", result.ErrorMessage);
        handler.AssertNoRequest(HttpMethod.Put, "/data-type");
    }

    [Fact]
    public async Task Example_ReadsARealOneOffTheInstance()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var handler = Wire.Routed(
            ("tree/document-type", $$"""{ "total": 1, "items": [ { "id": "{{id}}" } ] }"""),
            ($"document-type/{id}", $$"""{ "id": "{{id}}", "alias": "blogPost" }""")
        );
        var client = Wire.Client(handler);

        var example = await Umbraco.Cli.Commands.RawBodyCommand.ExampleAsync(
            client,
            Umbraco.Cli.Commands.SchemaNoun.DocumentTypes,
            CancellationToken.None
        );

        // A real body off the instance rather than a hand-maintained schema beside the
        // passthrough - a second definition of the shape is what goes stale silently.
        Assert.True(example.IsSuccess, example.ErrorMessage);
        Assert.Equal("blogPost", example.Data!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task Example_OnAnInstanceWithNone_PrintsTheBuiltInMinimalBody()
    {
        var handler = Wire.Routed(("tree/document-type", """{ "total": 0, "items": [] }"""));
        var client = Wire.Client(handler);

        var example = await Umbraco.Cli.Commands.RawBodyCommand.ExampleAsync(
            client,
            Umbraco.Cli.Commands.SchemaNoun.DocumentTypes,
            CancellationToken.None
        );

        // #200: a fresh site is when the shape is most needed, so it gets a body, not a 404.
        Assert.True(example.IsSuccess, example.ErrorMessage);
        Assert.Equal("myDocumentType", example.Data!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task Example_KindWithNoBuiltInBody_SaysSoRatherThanFailingObscurely()
    {
        var client = Wire.Client(Wire.Routed(("tree/template", """{ "total": 0, "items": [] }""")));

        var example = await Umbraco.Cli.Commands.RawBodyCommand.ExampleAsync(
            client,
            Umbraco.Cli.Commands.SchemaNoun.Templates,
            CancellationToken.None
        );

        Assert.Equal(404, example.StatusCode);
        Assert.Contains("Create one first", example.ErrorMessage);
    }

    /// <summary>
    /// The built-in bodies are a second definition of the create shape, which is the kind of copy
    /// that goes stale; this ties each one to the spec the client is generated from.
    /// </summary>
    [Theory]
    [InlineData(EntityKind.DocumentType, "CreateDocumentTypeRequestModel")]
    [InlineData(EntityKind.MediaType, "CreateMediaTypeRequestModel")]
    [InlineData(EntityKind.MemberType, "CreateMemberTypeRequestModel")]
    [InlineData(EntityKind.DataType, "CreateDataTypeRequestModel")]
    public void MinimalBody_CarriesEveryFieldTheSpecRequires(EntityKind kind, string model)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Umbraco.Cli.sln")))
            dir = dir.Parent;
        var spec = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(dir!.FullName, "spec", "management.json"))
        )!;
        var required = spec["components"]!["schemas"]![model]!["required"]!
            .AsArray()
            .Select(r => r!.GetValue<string>())
            .ToList();

        var body = Umbraco.Cli.Commands.RawBodyCommand.MinimalBody(kind)!.AsObject();

        Assert.Empty(required.Where(r => !body.ContainsKey(r)));
    }
}

/// <summary>
/// Resolve-then-replace, the composition <c>update --json-body --replace</c> makes out of the
/// resolver and <see cref="UmbracoManagementClient.MergeSchemaItemAsync"/> (#159, #201).
/// </summary>
internal static class SchemaUpdateByReference
{
    /// <summary>Resolves <paramref name="reference"/> and replaces that item with <paramref name="body"/>.</summary>
    /// <param name="client">The client.</param>
    /// <param name="kind">The schema kind.</param>
    /// <param name="reference">The id, alias or name.</param>
    /// <param name="body">The body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write's result, or the resolution failure.</returns>
    public static Task<UmbracoResponse<Empty>> UpdateByReferenceAsync(
        this UmbracoManagementClient client,
        EntityKind kind,
        string reference,
        JsonNode body,
        CancellationToken ct
    ) =>
        client.WithResolvedAsync(
            kind,
            reference,
            id => client.MergeSchemaItemAsync(kind, id, body, WriteMode.Replace, ct),
            ct
        );
}
