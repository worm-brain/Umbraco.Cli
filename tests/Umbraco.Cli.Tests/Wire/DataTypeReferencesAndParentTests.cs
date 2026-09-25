using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// #247: <c>data-types referenced-by</c> uses the list envelope with a readable <c>kind</c>, and
/// data types say which folder they are in, with <c>data-types list --parent</c> to list one.
/// </summary>
public class DataTypeReferencesAndParentTests
{
    private const string Raw = """
        { "total": 7, "items": [
          { "$type": "DocumentTypePropertyTypeReferenceResponseModel", "alias": "title", "name": "Title",
            "documentType": { "id": "11111111-1111-1111-1111-111111111111", "alias": "blogPost" } },
          { "$type": "MemberTypePropertyTypeReferenceResponseModel", "alias": "bio", "name": "Bio",
            "memberType": { "alias": "author" } }
        ] }
        """;

    [Fact]
    public void From_RawBody_ReplacesTypeWithAReadableKind()
    {
        var rows = DataTypeReferenceRows.From(JsonNode.Parse(Raw));

        Assert.Equal(
            ["documentTypePropertyType", "memberTypePropertyType"],
            rows.Items.Select(r => r["kind"]!.GetValue<string>())
        );
    }

    [Fact]
    public void From_RawBody_DropsTheDotNetDiscriminator()
    {
        var rows = DataTypeReferenceRows.From(JsonNode.Parse(Raw));

        Assert.DoesNotContain(rows.Items, r => r.ContainsKey("$type"));
    }

    [Fact]
    public void From_RawBody_KeepsTheServersTotalAndTheOtherFields()
    {
        var rows = DataTypeReferenceRows.From(JsonNode.Parse(Raw));

        Assert.Equal(
            (7, "blogPost"),
            (rows.Total, rows.Items.First()["documentType"]!["alias"]!.GetValue<string>())
        );
    }

    [Fact]
    public void From_NoBody_IsAnEmptyPage()
    {
        var rows = DataTypeReferenceRows.From(null);

        Assert.Equal((0, 0), (rows.Total, rows.Items.Count()));
    }

    [Fact]
    public void OwnerAlias_MemberTypeProperty_IsTheMemberType()
    {
        var row = DataTypeReferenceRows.From(JsonNode.Parse(Raw)).Items.ElementAt(1);

        Assert.Equal("author", DataTypeReferenceRows.OwnerAlias(row));
    }

    // ── parent ────────────────────────────────────────────────────────────────

    private static readonly Guid Folder = Guid.NewGuid();
    private static readonly Guid AtRoot = Guid.NewGuid();
    private static readonly Guid InFolder = Guid.NewGuid();

    /// <summary>A data-type tree with one type at the root and one inside a folder.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Tree() =>
        new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("tree/data-type/children"),
                HttpStatusCode.OK,
                $$"""{ "total": 1, "items": [ { "id": "{{InFolder}}", "name": "In folder", "parent": { "id": "{{Folder}}" } } ] }"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("tree/data-type/root"),
                HttpStatusCode.OK,
                $$"""{ "total": 2, "items": [ { "id": "{{Folder}}", "name": "F", "isFolder": true }, { "id": "{{AtRoot}}", "name": "At root" } ] }"""
            )
            .When(
                r => r.RequestUri!.AbsolutePath.Contains("/data-type/"),
                HttpStatusCode.OK,
                """{ "name": "X", "editorAlias": "Umbraco.TextBox", "values": [] }"""
            );

    [Fact]
    public async Task GetDataTypesAsync_TypeInAFolder_KeepsTheFolderAsItsParentThroughHydration()
    {
        var result = await Wire.Client(Tree()).GetDataTypesAsync(ct: CancellationToken.None);

        var inFolder = result.Data!.Items.Single(d => d.Id == InFolder);
        Assert.Equal(Folder, inFolder.Parent!.Id);
    }

    [Fact]
    public async Task GetDataTypesAsync_WithParent_ListsOnlyThatFoldersTypes()
    {
        var result = await Wire.Client(Tree())
            .GetDataTypesAsync(parentId: Folder, ct: CancellationToken.None);

        Assert.Equal((1, InFolder), (result.Data!.Total, Assert.Single(result.Data.Items).Id));
    }
}
