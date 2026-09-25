using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The shared <c>&lt;id|alias&gt;</c> resolver (#250 Phase 3) against the real client: each kind
/// reads its candidates where the alias or name really lives, an id costs no request, an unknown
/// reference is a 404 and an ambiguous name a 409.
/// </summary>
public class ReferenceResolverWireTests
{
    private static readonly Guid Master = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid BlogPost = Guid.Parse("11111111-0000-0000-0000-000000000002");

    /// <summary>A master template ("Master") with one child, "Blog Post" (alias blogPost).</summary>
    private static RoutingHandler Templates() =>
        Wire.Routed(
            (
                "tree/template/root",
                $$"""{"total":1,"items":[{"id":"{{Master}}","name":"Master","hasChildren":true}]}"""
            ),
            (
                "tree/template/children",
                $$"""{"total":1,"items":[{"id":"{{BlogPost}}","name":"Blog Post","hasChildren":false}]}"""
            ),
            (
                "item/template?",
                $$"""[{"id":"{{Master}}","alias":"master","name":"Master"},{"id":"{{BlogPost}}","alias":"blogPost","name":"Blog Post"}]"""
            )
        );

    [Fact]
    public async Task ResolveIdAsync_AnId_MakesNoRequest()
    {
        var handler = Wire.Blank();

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.Template, BlogPost.ToString());

        Assert.Equal((BlogPost, 0), (result.Data, handler.Recordings.Count));
    }

    [Fact]
    public async Task ResolveIdAsync_TemplateAliasThatDiffersFromItsName_Resolves()
    {
        // #206: the item search matches names, so "blogPost" (named "Blog Post") was never found.
        var result = await Wire.Client(Templates()).ResolveIdAsync(EntityKind.Template, "blogPost");

        Assert.Equal(BlogPost, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_TemplateByName_Resolves()
    {
        var result = await Wire.Client(Templates())
            .ResolveIdAsync(EntityKind.Template, "blog post");

        Assert.Equal(BlogPost, result.Data);
    }

    [Fact]
    public async Task GetTemplatesAsync_ListsNestedTemplatesWithTheirAliases()
    {
        // #206: the list read the tree root only (so Blog Post, under Master, was missing) and
        // never filled the alias.
        var result = await Wire.Client(Templates()).GetTemplatesAsync(0, 20);

        Assert.Equal(["master", "blogPost"], result.Data!.Items.Select(t => t.Alias));
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownTemplate_Is404NamingTheListCommand()
    {
        var result = await Wire.Client(Templates()).ResolveIdAsync(EntityKind.Template, "nope");

        Assert.Equal(404, result.StatusCode);
        Assert.Contains("umbraco templates list", result.ErrorMessage);
    }

    private static readonly Guid Brochure = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private static RoutingHandler MediaTypes() =>
        Wire.Routed(
            (
                "tree/media-type/root",
                $$"""{"total":1,"items":[{"id":"{{Brochure}}","name":"Brochure","isFolder":false}]}"""
            ),
            (
                $"media-type/{Brochure}",
                $$"""{"id":"{{Brochure}}","alias":"brochure","name":"Brochure"}"""
            )
        );

    [Theory]
    [InlineData("brochure")] // the alias (#222)
    [InlineData("Brochure")] // the name, which upload used to require
    public async Task ResolveIdAsync_MediaTypeByAliasOrName_Resolves(string reference)
    {
        var result = await Wire.Client(MediaTypes())
            .ResolveIdAsync(EntityKind.MediaType, reference);

        Assert.Equal(Brochure, result.Data);
    }

    [Fact]
    public async Task GetMediaTypesAsync_FillsTheAlias()
    {
        // #221: media-types list showed "alias": "" for every type.
        var result = await Wire.Client(MediaTypes()).GetMediaTypesAsync(0, 20);

        Assert.Equal("brochure", Assert.Single(result.Data!.Items).Alias);
    }

    [Fact]
    public async Task GetMemberTypesAsync_ListsTypesInsideFoldersWithTheirAliases()
    {
        // #213: the list read the tree root only and never filled the alias.
        var folder = Guid.NewGuid();
        var author = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "tree/member-type/root",
                $$"""{"total":1,"items":[{"id":"{{folder}}","name":"Site","isFolder":true}]}"""
            ),
            (
                "tree/member-type/children",
                $$"""{"total":1,"items":[{"id":"{{author}}","name":"Author","isFolder":false}]}"""
            ),
            ($"member-type/{author}", $$"""{"id":"{{author}}","alias":"author","name":"Author"}""")
        );

        var result = await Wire.Client(handler).GetMemberTypesAsync(0, 20);

        var type = Assert.Single(result.Data!.Items);
        Assert.Equal((author, "author"), (type.Id, type.Alias));
    }

    [Fact]
    public async Task ResolveIdAsync_TwoDataTypesWithTheName_IsRefusedWithBothIds()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "item/data-type/search",
                $$"""{"total":2,"items":[{"id":"{{a}}","name":"Tags"},{"id":"{{b}}","name":"tags"}]}"""
            )
        );

        var result = await Wire.Client(handler).ResolveIdAsync(EntityKind.DataType, "Tags");

        Assert.Equal(409, result.StatusCode);
        Assert.Contains(a.ToString(), result.ErrorMessage);
        Assert.Contains(b.ToString(), result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveIdAsync_MemberGroupByName_Resolves()
    {
        var subscribers = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "tree/member-group/root",
                $$"""{"total":1,"items":[{"id":"{{subscribers}}","name":"Subscribers"}]}"""
            )
        );

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.MemberGroup, "subscribers");

        Assert.Equal(subscribers, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_DictionaryKey_Resolves()
    {
        var minRead = Guid.NewGuid();
        var handler = Wire.Routed(
            ("dictionary", $$"""{"total":1,"items":[{"id":"{{minRead}}","name":"Blog.MinRead"}]}""")
        );

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.DictionaryItem, "Blog.MinRead");

        Assert.Equal(minRead, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_UserGroupAlias_WinsOverAnotherGroupsName()
    {
        // The alias is the stable key: a group whose NAME equals another's alias does not steal it.
        var editors = Guid.NewGuid();
        var impostor = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "user-group",
                $$"""{"total":2,"items":[{"id":"{{impostor}}","alias":"other","name":"blogEditors"},{"id":"{{editors}}","alias":"blogEditors","name":"Blog editors"}]}"""
            )
        );

        var result = await Wire.Client(handler).ResolveIdAsync(EntityKind.UserGroup, "blogEditors");

        Assert.Equal(editors, result.Data);
    }
}
