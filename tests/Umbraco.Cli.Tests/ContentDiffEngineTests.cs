using System.Text.Json.Nodes;
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
        Assert.Equal(ContentChangeKind.Added, added.Change);
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
        Assert.Equal(ContentChangeKind.Removed, removed.Change);
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
        Assert.Equal(ContentChangeKind.Drifted, drifted.Change);
        Assert.Equal(id, drifted.Id);
        Assert.Empty(diff.Changed);
        Assert.False(diff.HasChanges); // apply cannot converge drift, so nothing to do
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
}
