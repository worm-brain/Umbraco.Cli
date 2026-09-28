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
    public async Task ResolveIdAsync_UnknownTemplate_IsInvalidArgumentNamingTheListCommand()
    {
        var result = await Wire.Client(Templates()).ResolveIdAsync(EntityKind.Template, "nope");

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.Contains("umbraco template list", result.ErrorMessage);
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

    private static readonly Guid TextPage = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Landing = Guid.Parse("22222222-0000-0000-0000-000000000003");
    private static readonly Guid Other = Guid.Parse("22222222-0000-0000-0000-000000000004");

    /// <summary>
    /// Three document types: "Text Page" (textPage), "Landing" (landing), and one named "landing"
    /// by display name only, whose alias is "other".
    /// </summary>
    private static RoutingHandler DocumentTypes() =>
        Wire.Routed(
            (
                "tree/document-type/root",
                $$"""{"total":3,"items":[{"id":"{{TextPage}}","name":"Text Page","isFolder":false},{"id":"{{Landing}}","name":"Landing","isFolder":false},{"id":"{{Other}}","name":"landing","isFolder":false}]}"""
            ),
            (
                $"document-type/{TextPage}",
                $$"""{"id":"{{TextPage}}","alias":"textPage","name":"Text Page"}"""
            ),
            (
                $"document-type/{Landing}",
                $$"""{"id":"{{Landing}}","alias":"landing","name":"Landing"}"""
            ),
            ($"document-type/{Other}", $$"""{"id":"{{Other}}","alias":"other","name":"landing"}""")
        );

    [Theory]
    [InlineData("textPage")] // the alias
    [InlineData("text page")] // the name, ignoring case (#358)
    public async Task ResolveIdAsync_DocumentTypeByAliasOrName_Resolves(string reference)
    {
        var result = await Wire.Client(DocumentTypes())
            .ResolveIdAsync(EntityKind.DocumentType, reference);

        Assert.Equal(TextPage, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_DocumentTypeAliasThatIsAlsoAName_PrefersTheAlias()
    {
        var result = await Wire.Client(DocumentTypes())
            .ResolveIdAsync(EntityKind.DocumentType, "landing");

        Assert.Equal(Landing, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownDocumentType_IsInvalidArgumentNamingTheListCommand()
    {
        var result = await Wire.Client(DocumentTypes())
            .ResolveIdAsync(EntityKind.DocumentType, "nope");

        Assert.Equal(
            (FailureCategory.InvalidArgument, true),
            (result.Category, result.ErrorMessage!.Contains("umbraco document-type list"))
        );
    }

    [Fact]
    public async Task GetMediaTypesAsync_FillsTheAlias()
    {
        // #221: media-type list showed "alias": "" for every type.
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

    private static readonly Guid Author = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid Editor = Guid.Parse("44444444-0000-0000-0000-000000000002");
    private static readonly Guid Reviewer = Guid.Parse("44444444-0000-0000-0000-000000000003");

    /// <summary>
    /// Three member types: "Site Author" (siteAuthor), and two named "Staff" (editor, reviewer).
    /// </summary>
    private static RoutingHandler MemberTypes() =>
        Wire.Routed(
            (
                "tree/member-type/root",
                $$"""{"total":3,"items":[{"id":"{{Author}}","name":"Site Author","isFolder":false},{"id":"{{Editor}}","name":"Staff","isFolder":false},{"id":"{{Reviewer}}","name":"Staff","isFolder":false}]}"""
            ),
            (
                $"member-type/{Author}",
                $$"""{"id":"{{Author}}","alias":"siteAuthor","name":"Site Author"}"""
            ),
            ($"member-type/{Editor}", $$"""{"id":"{{Editor}}","alias":"editor","name":"Staff"}"""),
            (
                $"member-type/{Reviewer}",
                $$"""{"id":"{{Reviewer}}","alias":"reviewer","name":"Staff"}"""
            )
        );

    [Theory]
    [InlineData("siteAuthor")] // the alias
    [InlineData("site author")] // the name, ignoring case
    public async Task ResolveIdAsync_MemberTypeByAliasOrName_Resolves(string reference)
    {
        var result = await Wire.Client(MemberTypes())
            .ResolveIdAsync(EntityKind.MemberType, reference);

        Assert.Equal(Author, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_MemberTypeNameTwoTypesShare_IsRefusedListingBoth()
    {
        var result = await Wire.Client(MemberTypes())
            .ResolveIdAsync(EntityKind.MemberType, "staff");

        Assert.Equal(
            (FailureCategory.InvalidArgument, true, true),
            (
                result.Category,
                result.ErrorMessage!.Contains(Editor.ToString()),
                result.ErrorMessage.Contains(Reviewer.ToString())
            )
        );
    }

    [Fact]
    public async Task GetDocumentTypesAsync_TypeInsideAFolder_IsListedWithItsAlias()
    {
        // document-type list showed "alias": "" for every type: the tree items carry no alias,
        // so each type is read by id, as media and member types are.
        var folder = Guid.NewGuid();
        var textPage = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "tree/document-type/root",
                $$"""{"total":1,"items":[{"id":"{{folder}}","name":"Pages","isFolder":true}]}"""
            ),
            (
                "tree/document-type/children",
                $$"""{"total":1,"items":[{"id":"{{textPage}}","name":"Text Page","isFolder":false,"isElement":false}]}"""
            ),
            (
                $"document-type/{textPage}",
                $$"""{"id":"{{textPage}}","alias":"textPage","name":"Text Page"}"""
            )
        );

        var result = await Wire.Client(handler).GetDocumentTypesAsync(0, 20);

        var type = Assert.Single(result.Data!.Items);
        Assert.Equal((textPage, "textPage"), (type.Id, type.Alias));
    }

    [Fact]
    public async Task GetDocumentTypesAsync_ByIdBodyWithoutAlias_ListsTheTypeWithAnEmptyAlias()
    {
        // A by-id body without an alias still lists the type, with "" rather than a made-up alias.
        var textPage = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "tree/document-type/root",
                $$"""{"total":1,"items":[{"id":"{{textPage}}","name":"Text Page","isFolder":false}]}"""
            ),
            ($"document-type/{textPage}", "{}")
        );

        var result = await Wire.Client(handler).GetDocumentTypesAsync(0, 20);

        Assert.Equal("", Assert.Single(result.Data!.Items).Alias);
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

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        Assert.Contains(a.ToString(), result.ErrorMessage);
        Assert.Contains(b.ToString(), result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveIdAsync_TheSameReferenceTwice_LooksItUpOnce()
    {
        // A delete's in-use check and the delete itself both resolve the argument.
        var tags = Guid.NewGuid();
        var handler = Wire.Routed(
            ("item/data-type/search", $$"""{"total":1,"items":[{"id":"{{tags}}","name":"Tags"}]}""")
        );
        var client = Wire.Client(handler);

        await client.ResolveIdAsync(EntityKind.DataType, "Tags");
        var second = await client.ResolveIdAsync(EntityKind.DataType, "Tags");

        Assert.Equal((tags, 1), (second.Data, handler.Recordings.Count));
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

    [Theory]
    [InlineData("relateDocumentOnCopy")]
    [InlineData("Relate Document On Copy")]
    public async Task ResolveIdAsync_RelationTypeByAliasOrName_Resolves(string reference)
    {
        // #300: relation-type list shows the alias, so the relation commands take it.
        var onCopy = Guid.NewGuid();
        var handler = Wire.Routed(
            (
                "relation-type",
                $$"""{"total":1,"items":[{"id":"{{onCopy}}","alias":"relateDocumentOnCopy","name":"Relate Document On Copy"}]}"""
            )
        );

        var result = await Wire.Client(handler).ResolveIdAsync(EntityKind.RelationType, reference);

        Assert.Equal(onCopy, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownRelationType_IsInvalidArgumentNamingTheListCommand()
    {
        var handler = Wire.Routed(("relation-type", """{"total":0,"items":[]}"""));

        var result = await Wire.Client(handler).ResolveIdAsync(EntityKind.RelationType, "nope");

        Assert.Contains("umbraco relation-type list", result.ErrorMessage);
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
