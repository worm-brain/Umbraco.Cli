using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>content create --json-body</c> takes the CLI's body and the Management API's (#241), so the
/// output of <c>document-blueprint scaffold</c> can be piped straight into a create.
/// </summary>
public class ContentCreateBodyTests
{
    private static readonly Guid TypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // What 'document-blueprint scaffold' prints: the API shape, without the blueprint's id (#299).
    private static readonly string Scaffold = $$"""
        { "documentType": { "id": "{{TypeId}}" }, "flags": [],
          "values": [ { "alias": "title", "value": "Starter", "editorAlias": "Umbraco.TextBox" } ],
          "variants": [ { "culture": null, "name": "Starter", "state": "Draft" } ] }
        """;

    [Fact]
    public void ReadCreateRequest_ScaffoldShape_ReadsDocumentTypeAsTheContentType()
    {
        var request = ContentCreateCommand.ReadCreateRequest(Scaffold, null);

        Assert.Equal(TypeId, request.ContentType.Id);
    }

    [Fact]
    public void ReadCreateRequest_ApiShapeWithAnId_KeepsIt()
    {
        // #299: the id was dropped from API-shaped bodies, so exported ids were lost and a
        // retried create made a duplicate instead of a 409.
        var id = Guid.NewGuid();

        var request = ContentCreateCommand.ReadCreateRequest(
            $$"""{ "id": "{{id}}", "documentType": { "alias": "blogYear" }, "variants": [ { "name": "2026" } ] }""",
            null
        );

        Assert.Equal(id, request.Id);
    }

    [Fact]
    public void ReadCreateRequest_ApiShapeIdContradictsIdFlag_IsRefused()
    {
        var body = $$"""{ "id": "{{Guid.NewGuid()}}", "documentType": { "alias": "blogYear" } }""";

        Assert.Throws<InvalidInputException>(() =>
            ContentCreateCommand.ReadCreateRequest(body, Guid.NewGuid())
        );
    }

    [Fact]
    public void ReadCreateRequest_ScaffoldShape_KeepsValuesAndVariants()
    {
        var request = ContentCreateCommand.ReadCreateRequest(Scaffold, null);

        Assert.Equal(
            ("title", "Starter"),
            (request.Values.Single().Alias, request.Variants.Single().Name)
        );
    }

    [Fact]
    public void ReadCreateRequest_CliShapeWithAnId_KeepsItForAnIdempotentCreate()
    {
        var id = Guid.NewGuid();

        var request = ContentCreateCommand.ReadCreateRequest(
            $$"""{ "id": "{{id}}", "contentType": { "alias": "blogPost" }, "variants": [ { "name": "P" } ] }""",
            null
        );

        Assert.Equal(id, request.Id);
    }

    [Fact]
    public void ReadCreateRequest_IdFlag_FillsTheId()
    {
        var id = Guid.NewGuid();

        var request = ContentCreateCommand.ReadCreateRequest(Scaffold, id);

        Assert.Equal(id, request.Id);
    }

    [Fact]
    public void ReadCreateRequest_IdFlagContradictsTheBody_IsRefused()
    {
        var body = $$"""{ "id": "{{Guid.NewGuid()}}", "contentType": { "alias": "blogPost" } }""";

        var ex = Assert.Throws<InvalidInputException>(() =>
            ContentCreateCommand.ReadCreateRequest(body, Guid.NewGuid())
        );

        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public void ReadCreateRequest_NoDocumentType_SaysWhatToSet()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            ContentCreateCommand.ReadCreateRequest("""{ "variants": [ { "name": "P" } ] }""", null)
        );

        Assert.Contains("contentType", ex.Message);
    }
}
