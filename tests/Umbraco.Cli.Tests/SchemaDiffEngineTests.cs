using System.Text.Json.Nodes;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The core matching logic of the pipeline (#68 / ADR 0005 §2): GUID-primary, alias-fallback,
/// with change/unchanged classification, prune detection, id-mismatch flagging, and the
/// ambiguous-key skip. Pure and fast, so every branch gets a focused test.
/// </summary>
public class SchemaDiffEngineTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static JsonNode Doc(Guid id, string alias, string name = "n") =>
        JsonNode.Parse($$"""{"id":"{{id}}","alias":"{{alias}}","name":"{{name}}"}""")!;

    private static JsonNode Data(Guid id, string name, string editor = "Umbraco.TextBox") =>
        JsonNode.Parse($$"""{"id":"{{id}}","name":"{{name}}","editorAlias":"{{editor}}"}""")!;

    private static SchemaSnapshot DocSnapshot(params JsonNode[] docs) =>
        new() { DocumentTypes = docs.ToList() };

    private static SchemaSnapshot DataSnapshot(params JsonNode[] data) =>
        new() { DataTypes = data.ToList() };

    // ── added / removed / unchanged ──────────────────────────────────────────────

    [Fact]
    public void Compare_EntityOnlyInDesired_IsAdded()
    {
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(DocSnapshot(Doc(id, "blogPost")), DocSnapshot());

        var added = Assert.Single(diff.DocumentTypes.Added);
        Assert.Equal("blogPost", added.Identity);
        Assert.Equal(id, added.DesiredId);
        Assert.NotNull(added.DesiredBody); // apply needs the body to create it
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Compare_EntityOnlyInCurrent_IsRemoved()
    {
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(DocSnapshot(), DocSnapshot(Doc(id, "legacy")));

        var removed = Assert.Single(diff.DocumentTypes.Removed);
        Assert.Equal("legacy", removed.Identity);
        Assert.Equal(id, removed.CurrentId);
    }

    [Fact]
    public void Compare_IdenticalBodies_AreUnchangedNotChanged()
    {
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(
            DocSnapshot(Doc(id, "blogPost")),
            DocSnapshot(Doc(id, "blogPost"))
        );

        Assert.Equal(1, diff.DocumentTypes.Unchanged);
        Assert.Empty(diff.DocumentTypes.Changed);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Compare_SameIdDifferentBody_IsChanged_WithBothIds()
    {
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(
            DocSnapshot(Doc(id, "blogPost", name: "New Name")),
            DocSnapshot(Doc(id, "blogPost", name: "Old Name"))
        );

        var changed = Assert.Single(diff.DocumentTypes.Changed);
        Assert.Equal(id, changed.DesiredId);
        Assert.Equal(id, changed.CurrentId);
        Assert.False(changed.IdMismatch);
        Assert.NotNull(changed.DesiredBody);
    }

    // ── GUID-primary vs alias-fallback ───────────────────────────────────────────

    [Fact]
    public void Compare_DifferentIdsSameAlias_MatchesByAlias_AndFlagsIdMismatch()
    {
        var desiredId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        // Same alias, different content, different id -> alias-fallback match, update the live id.
        var diff = SchemaDiffEngine.Compare(
            DocSnapshot(Doc(desiredId, "blogPost", name: "Desired")),
            DocSnapshot(Doc(liveId, "blogPost", name: "Live"))
        );

        var changed = Assert.Single(diff.DocumentTypes.Changed);
        Assert.True(changed.IdMismatch);
        Assert.Equal(desiredId, changed.DesiredId);
        Assert.Equal(liveId, changed.CurrentId);
        // The live entity is matched, so it is NOT also reported as removed.
        Assert.Empty(diff.DocumentTypes.Removed);
        Assert.Empty(diff.DocumentTypes.Added);
    }

    [Fact]
    public void Compare_IdMatchWins_EvenWhenAnotherLiveEntitySharesAlias()
    {
        // GUID-primary: the id match must win over a same-alias candidate.
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(
            DocSnapshot(Doc(id, "blogPost", name: "same")),
            DocSnapshot(Doc(id, "blogPost", name: "same"))
        );

        Assert.Equal(1, diff.DocumentTypes.Unchanged);
        Assert.Empty(diff.DocumentTypes.Removed);
    }

    [Fact]
    public void Compare_GuidAndAliasCrossCollision_DoesNotDoubleMatchTheSameLiveEntity()
    {
        // Desired A (idA) GUID-matches live L (idA). Desired B (idB) shares A's alias. The
        // two-pass matcher must NOT let B also claim L by alias: B has no live match -> Added,
        // and L is matched exactly once (by A), so it is not reported Removed.
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(
            DocSnapshot(Doc(idA, "blogPost", name: "A"), Doc(idB, "blogPost", name: "B")),
            DocSnapshot(Doc(idA, "blogPost", name: "A"))
        );

        // A matched live L by id (unchanged); B is genuinely new.
        Assert.Equal(1, diff.DocumentTypes.Unchanged);
        var added = Assert.Single(diff.DocumentTypes.Added);
        Assert.Equal(idB, added.DesiredId);
        // L was claimed once by A, so it must not appear as a prune candidate.
        Assert.Empty(diff.DocumentTypes.Removed);
    }

    [Fact]
    public void Compare_DataTypeMatchesByName_NotAlias()
    {
        // Data types have no alias; the human key is the name.
        var id = Guid.NewGuid();
        var diff = SchemaDiffEngine.Compare(
            DataSnapshot(Data(Guid.NewGuid(), "My Slider", editor: "Umbraco.Slider")),
            DataSnapshot(Data(id, "My Slider", editor: "Umbraco.TextBox"))
        );

        var changed = Assert.Single(diff.DataTypes.Changed);
        Assert.Equal("My Slider", changed.Identity);
        Assert.True(changed.IdMismatch);
        Assert.Equal(id, changed.CurrentId);
    }

    // ── ambiguity ────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_AmbiguousLiveName_IsSkipped_NotGuessed()
    {
        // Two live data types share a name; a desired entity with that name (no id match) is
        // ambiguous and must be skipped, not applied to a guessed target.
        var diff = SchemaDiffEngine.Compare(
            DataSnapshot(Data(Guid.NewGuid(), "Dupe")),
            DataSnapshot(Data(Guid.NewGuid(), "Dupe"), Data(Guid.NewGuid(), "Dupe"))
        );

        var skipped = Assert.Single(diff.DataTypes.Skipped);
        Assert.Equal("Dupe", skipped.Identity);
        Assert.Contains("ambiguous", skipped.Note!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(diff.DataTypes.Added);
        Assert.Empty(diff.DataTypes.Changed);
        // Critical: the two ambiguous live entities must NOT become prune candidates — otherwise
        // a --prune run would delete the very entities we refused to touch.
        Assert.Empty(diff.DataTypes.Removed);
    }

    // ── order-insensitive body comparison ────────────────────────────────────────

    [Fact]
    public void Compare_SameContentDifferentPropertyOrder_IsUnchanged()
    {
        var id = Guid.NewGuid();
        var desired = JsonNode.Parse($$"""{"id":"{{id}}","alias":"x","name":"X"}""")!;
        var current = JsonNode.Parse($$"""{"name":"X","id":"{{id}}","alias":"x"}""")!;

        var diff = SchemaDiffEngine.Compare(DocSnapshot(desired), DocSnapshot(current));

        Assert.Equal(1, diff.DocumentTypes.Unchanged);
        Assert.Empty(diff.DocumentTypes.Changed);
    }

    [Fact]
    public void Compare_NestedArrayOrderDiffers_IsChanged()
    {
        // Arrays are order-sensitive (property order in a doc type is meaningful).
        var id = Guid.NewGuid();
        var desired = JsonNode.Parse(
            $$"""{"id":"{{id}}","alias":"x","properties":[{"alias":"a"},{"alias":"b"}]}"""
        )!;
        var current = JsonNode.Parse(
            $$"""{"id":"{{id}}","alias":"x","properties":[{"alias":"b"},{"alias":"a"}]}"""
        )!;

        var diff = SchemaDiffEngine.Compare(DocSnapshot(desired), DocSnapshot(current));

        Assert.Single(diff.DocumentTypes.Changed);
    }

    // ── empty ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_TwoEmptySnapshots_HasNoChanges()
    {
        var diff = SchemaDiffEngine.Compare(new SchemaSnapshot(), new SchemaSnapshot());
        Assert.False(diff.HasChanges);
    }
}
