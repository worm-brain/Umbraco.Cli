using System.Text.Json.Nodes;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The GUID-only matching core of the content pipeline (#100): a document is added when it is in
/// the snapshot but not live, removed when live but not in the snapshot, changed when the body (or
/// placement) differs, and unchanged when identical. Placement drift is flagged but still classed
/// as changed.
/// </summary>
public class ContentDiffEngineTests
{
    private static ContentNode Doc(Guid id, Guid? parent, string name) =>
        new()
        {
            Id = id,
            Parent = parent,
            Body = JsonNode.Parse($$"""{"id":"{{id}}","name":"{{name}}"}""")!,
        };

    private static ContentSnapshot Snap(params ContentNode[] docs) =>
        new() { Documents = [.. docs] };

    [Fact]
    public void Compare_DocumentInSnapshotNotLive_IsAdded()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(Snap(Doc(id, parent, "New")), Snap());

        var added = Assert.Single(diff.Added);
        Assert.Equal(TreeChangeKind.Added, added.Change);
        Assert.Equal(id, added.Id);
        Assert.Equal(parent, added.Parent);
        Assert.NotNull(added.DesiredBody); // carried through for the create
    }

    [Fact]
    public void Compare_DocumentLiveNotInSnapshot_IsRemoved()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(Snap(), Snap(Doc(id, null, "Old")));

        var removed = Assert.Single(diff.Removed);
        Assert.Equal(TreeChangeKind.Removed, removed.Change);
        Assert.Equal(id, removed.Id);
    }

    [Fact]
    public void Compare_RemovedDocument_CarriesItsDocumentTypeAndLiveParent()
    {
        // #225: a prune can only exclude types and subtrees if the diff keeps them.
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var type = Guid.NewGuid();
        var live = new ContentNode
        {
            Id = id,
            Parent = parent,
            Body = JsonNode.Parse($$$"""{"id":"{{{id}}}","documentType":{"id":"{{{type}}}"}}""")!,
        };

        var diff = ContentDiffEngine.Compare(Snap(), Snap(live));

        Assert.Equal(type, Assert.Single(diff.Removed).DocumentTypeId);
        Assert.Equal(parent, diff.LiveParents[id]);
    }

    [Fact]
    public void Compare_SameIdDifferentBody_IsChanged()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(
            Snap(Doc(id, null, "Renamed")),
            Snap(Doc(id, null, "Original"))
        );

        var changed = Assert.Single(diff.Changed);
        Assert.Equal(id, changed.Id);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Drifted);
    }

    [Fact]
    public void Compare_IdenticalDocument_IsUnchanged()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(
            Snap(Doc(id, null, "Same")),
            Snap(Doc(id, null, "Same"))
        );

        Assert.Equal(1, diff.Unchanged);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Compare_SameBodyDifferentParent_IsDriftedNotChanged()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(
            Snap(Doc(id, Guid.NewGuid(), "Same")),
            Snap(Doc(id, null, "Same"))
        );

        // Placement-only drift is advisory: reported as Drifted, never an actionable change.
        var drifted = Assert.Single(diff.Drifted);
        Assert.Equal(TreeChangeKind.Drifted, drifted.Change);
        Assert.Equal(id, drifted.Id);
        Assert.Empty(diff.Changed);
        Assert.False(diff.HasChanges); // apply cannot converge drift, so nothing to do
    }

    [Fact]
    public void Compare_OnlyPublishStateDiffers_IsAStateOnlyChange()
    {
        // #223: published on the source, a draft on the target, same content.
        var id = Guid.NewGuid();
        ContentNode In(string state) =>
            new()
            {
                Id = id,
                Body = JsonNode.Parse(
                    $$"""{"id":"{{id}}","variants":[{"culture":null,"name":"Home","state":"{{state}}"}]}"""
                )!,
            };

        var diff = ContentDiffEngine.Compare(Snap(In("Published")), Snap(In("Draft")));

        var changed = Assert.Single(diff.Changed);
        Assert.False(changed.BodyChanged, "the body is the same");
        Assert.Same(PublishScope.WholeDocument, changed.State.Publish);
    }

    [Fact]
    public void Compare_AddedPublishedDocument_CarriesItsPublishStep()
    {
        var id = Guid.NewGuid();
        var doc = new ContentNode
        {
            Id = id,
            Body = JsonNode.Parse(
                $$"""{"id":"{{id}}","variants":[{"culture":"en-US","state":"Published"}]}"""
            )!,
        };

        var diff = ContentDiffEngine.Compare(Snap(doc), Snap());

        Assert.Equal(["en-US"], Assert.Single(diff.Added).State.Publish!.Cultures!);
    }

    [Fact]
    public void Compare_ChangedDocument_CarriesTheNormalisedBodyForApply()
    {
        // #224: the body apply sends is the one the diff compared, without the source's dates.
        var id = Guid.NewGuid();
        var desired = new ContentNode
        {
            Id = id,
            Body = JsonNode.Parse(
                $$"""{"id":"{{id}}","isTrashed":false,"variants":[{"culture":null,"name":"New","updateDate":"2026-09-01T00:00:00Z"}]}"""
            )!,
        };

        var diff = ContentDiffEngine.Compare(Snap(desired), Snap(Doc(id, null, "Old")));

        Assert.Equal(
            $$"""{"id":"{{id}}","variants":[{"culture":null,"name":"New"}]}""",
            Assert.Single(diff.Changed).DesiredBody!.ToJsonString()
        );
    }

    [Fact]
    public void Compare_OnlyInstanceDatesDiffer_IsUnchanged()
    {
        // #224: the same document on two instances always has different dates; that is not a change.
        var id = Guid.NewGuid();
        ContentNode At(string date) =>
            new()
            {
                Id = id,
                Body = JsonNode.Parse(
                    $$"""{"id":"{{id}}","variants":[{"culture":null,"name":"Home","updateDate":"{{date}}"}]}"""
                )!,
            };

        var diff = ContentDiffEngine.Compare(
            Snap(At("2026-09-01T00:00:00Z")),
            Snap(At("2026-09-24T00:00:00Z"))
        );

        Assert.Equal(1, diff.Unchanged);
    }

    // ── #291: Label values; #293: names and types on every row ──────────────

    /// <summary>A document with a Label value and a title, of the given type.</summary>
    private static ContentNode Labelled(Guid id, string submittedAt, string title = "Hi") =>
        new()
        {
            Id = id,
            Body = JsonNode.Parse(
                $$"""
                {"id":"{{id}}","documentType":{"id":"{{PageType}}"},
                 "values":[
                   {"editorAlias":"Umbraco.Label","alias":"submittedAt","culture":null,"segment":null,"value":"{{submittedAt}}"},
                   {"editorAlias":"Umbraco.TextBox","alias":"title","culture":null,"segment":null,"value":"{{title}}"}],
                 "variants":[{"culture":"da-DK","name":"Kontakt"},{"culture":"en-US","name":"Contact"}]}
                """
            )!,
        };

    private static readonly Guid PageType = Guid.NewGuid();

    [Fact]
    public void Compare_OnlyALabelValueDiffers_IsUnchanged()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(
            Snap(Labelled(id, "2026-09-01")),
            Snap(Labelled(id, "2026-09-28"))
        );

        Assert.Equal(1, diff.Unchanged);
    }

    [Fact]
    public void Compare_ALabelValueDiffers_IsCountedAsNotPromoted()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        var diff = ContentDiffEngine.Compare(
            Snap(Labelled(a, "2026-09-01"), Labelled(b, "2026-09-02")),
            Snap(Labelled(a, "2026-09-28"), Labelled(b, "2026-09-02"))
        );

        Assert.Equal(1, diff.UnpromotedValues["submittedAt"]);
    }

    [Fact]
    public void Compare_ALabelValueOnAnAddedDocument_IsCountedAsNotPromoted()
    {
        var diff = ContentDiffEngine.Compare(Snap(Labelled(Guid.NewGuid(), "2026-09-01")), Snap());

        Assert.Equal(1, diff.UnpromotedValues["submittedAt"]);
    }

    [Fact]
    public void Compare_ATextValueDiffersBesideALabel_IsChangedWithoutTheLabel()
    {
        var id = Guid.NewGuid();

        var diff = ContentDiffEngine.Compare(
            Snap(Labelled(id, "2026-09-01", title: "Hello")),
            Snap(Labelled(id, "2026-09-28", title: "Hi"))
        );

        Assert.Equal(["values.title"], Assert.Single(diff.Changed).Changes);
    }

    [Theory]
    [InlineData("en-US", "Contact")]
    [InlineData("da-DK", "Kontakt")]
    [InlineData(null, "Kontakt")] // no default language known: the first variant
    public void Compare_RemovedDocument_IsNamedInTheDefaultLanguage(string? culture, string name)
    {
        // #293: a prune plan must say what it deletes, not only the id.
        var diff = ContentDiffEngine.Compare(
            Snap(),
            Snap(Labelled(Guid.NewGuid(), "x")),
            defaultCulture: culture
        );

        Assert.Equal(name, Assert.Single(diff.Rows).Name);
    }

    [Fact]
    public void Compare_AddedDocument_CarriesItsDocumentTypeId()
    {
        var diff = ContentDiffEngine.Compare(Snap(Labelled(Guid.NewGuid(), "x")), Snap());

        Assert.Equal(PageType, Assert.Single(diff.Added).DocumentTypeId);
    }

    [Fact]
    public void NameOf_InvariantDocument_IsTheInvariantVariantsName()
    {
        var body = JsonNode.Parse("""{"variants":[{"culture":null,"name":"Home"}]}""");

        Assert.Equal("Home", ContentDiffEngine.NameOf(body, "en-US"));
    }

    [Fact]
    public void NameOf_NoVariants_IsNull()
    {
        Assert.Null(ContentDiffEngine.NameOf(JsonNode.Parse("{}"), "en-US"));
    }
}
