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
    public async Task CreateDocumentTypeRawAsync_SendsTheBodyVerbatim()
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
            .CreateDocumentTypeRawAsync(body, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // Verbatim: nothing between the caller's body and the API, which is the point - a typed
        // record in the middle is what dropped properties and groups in the first place.
        var sent = handler.BodyOf(HttpMethod.Post, "/document-type");
        Assert.Single(sent["properties"]!.AsArray());
        Assert.Single(sent["containers"]!.AsArray());
        Assert.True(sent["variesByCulture"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateDataTypeRawAsync_CanSetTheEditorConfiguration()
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

        await Wire.Client(handler).UpdateDataTypeRawAsync(id, body, CancellationToken.None);

        // #169: `values` is the dropdown's items. The typed update deliberately never exposed it,
        // so setting it needed a schema round-trip.
        var sent = handler.BodyOf(HttpMethod.Put, $"/data-type/{id}");
        var items = Assert.Single(sent["values"]!.AsArray());
        Assert.Equal(
            ["News", "Opinion"],
            items!["value"]!.AsArray().Select(v => v!.GetValue<string>())
        );
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
            client.GetDocumentTypeIdsAsync,
            client.GetDocumentTypeRawAsync,
            "document types",
            CancellationToken.None
        );

        // A real body off the instance rather than a hand-maintained schema beside the
        // passthrough - a second definition of the shape is what goes stale silently.
        Assert.True(example.IsSuccess, example.ErrorMessage);
        Assert.Equal("blogPost", example.Data!["alias"]!.GetValue<string>());
    }

    [Fact]
    public async Task Example_OnAnInstanceWithNone_SaysSoRatherThanFailingObscurely()
    {
        var handler = Wire.Routed(("tree/document-type", """{ "total": 0, "items": [] }"""));
        var client = Wire.Client(handler);

        var example = await Umbraco.Cli.Commands.RawBodyCommand.ExampleAsync(
            client,
            client.GetDocumentTypeIdsAsync,
            client.GetDocumentTypeRawAsync,
            "document types",
            CancellationToken.None
        );

        Assert.False(example.IsSuccess);
        Assert.Contains("no document types", example.ErrorMessage);
        Assert.Contains("Create one first", example.ErrorMessage);
    }
}
