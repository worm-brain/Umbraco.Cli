using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// List rows and <c>get</c> agree (#202), and <c>get</c> says where an item lives (#205). The tree
/// rows behind <c>content list</c> have no update date and only a type id, and the by-id bodies
/// behind <c>content get</c> / <c>media get</c> have no parent at all.
/// </summary>
public class ListGetParityWireTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Type = Guid.NewGuid();

    [Fact]
    public async Task GetContentAsync_TreeRows_CarryTheDocumentTypeAlias()
    {
        var handler = Wire.Routed(
            ($"/document-type/{Type}", """{ "alias": "blogPost" }"""),
            (
                "tree/document/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "documentType": { "id": "{{Type}}" }, "variants": [ { "name": "Post" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler).GetContentAsync(ct: CancellationToken.None);

        Assert.Equal("blogPost", Assert.Single(result.Data!.Items).DocumentType!.Alias);
    }

    [Fact]
    public async Task GetContentAsync_TreeRows_HaveNoUpdateDateRatherThanADefaultOne()
    {
        var handler = Wire.Routed(
            (
                "tree/document/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "variants": [ { "name": "Post" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler).GetContentAsync(ct: CancellationToken.None);

        Assert.Null(Assert.Single(result.Data!.Items).UpdateDate);
    }

    [Fact]
    public async Task GetMediaAsync_TreeRows_CarryTheMediaTypeAlias()
    {
        var handler = Wire.Routed(
            ($"/media-type/{Type}", """{ "alias": "brochure" }"""),
            (
                "tree/media/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "mediaType": { "id": "{{Type}}" }, "variants": [ { "name": "PDF" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler).GetMediaAsync(ct: CancellationToken.None);

        Assert.Equal("brochure", Assert.Single(result.Data!.Items).MediaType!.Alias);
    }

    [Fact]
    public async Task GetContentByIdAsync_ChildDocument_ReportsItsParentFromTheTree()
    {
        var handler = Wire.Routed(
            (
                "tree/document/ancestors",
                $$"""[ { "id": "{{Parent}}" }, { "id": "{{Item}}", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            ($"/document/{Item}", $$"""{ "id": "{{Item}}", "variants": [ { "name": "Post" } ] }""")
        );

        var result = await Wire.Client(handler).GetContentByIdAsync(Item, CancellationToken.None);

        Assert.Equal(Parent, result.Data!.Parent!.Id);
    }

    [Fact]
    public async Task GetContentByIdAsync_AncestorsReadFails_StillReturnsTheDocument()
    {
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.Contains("ancestors"),
                HttpStatusCode.InternalServerError,
                ""
            )
            .When(
                _ => true,
                HttpStatusCode.OK,
                $$"""{ "id": "{{Item}}", "variants": [ { "name": "Post" } ] }"""
            );

        var result = await Wire.Client(handler).GetContentByIdAsync(Item, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(result.Data!.Parent);
    }

    [Fact]
    public async Task GetMediaByIdAsync_ItemInAFolder_ReportsTheFolder()
    {
        var handler = Wire.Routed(
            (
                "tree/media/ancestors",
                $$"""[ { "id": "{{Parent}}" }, { "id": "{{Item}}", "parent": { "id": "{{Parent}}" } } ]"""
            ),
            ($"/media/{Item}", $$"""{ "id": "{{Item}}", "variants": [ { "name": "PDF" } ] }""")
        );

        var result = await Wire.Client(handler).GetMediaByIdAsync(Item, CancellationToken.None);

        Assert.Equal(Parent, result.Data!.Parent!.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ParentIn_PicksTheParent(bool selfListed, bool hasParent)
    {
        var chain = new List<(Guid?, Guid?)>();
        if (hasParent)
            chain.Add((Parent, null));
        if (selfListed)
            chain.Add((Item, hasParent ? Parent : null));

        var parent = UmbracoManagementClient.ParentIn(chain, Item);

        Assert.Equal(hasParent ? Parent : null, parent?.Id);
    }
}
