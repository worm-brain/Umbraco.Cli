using System.Text.Json.Nodes;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The classify loop shared by the content and media pipelines (#274), driven through a minimal
/// node type so the tests pin the loop itself rather than either pipeline's normaliser.
/// </summary>
public class SnapshotTreeDiffTests
{
    /// <summary>A bare tree node: an id, a parent and a body.</summary>
    private sealed record Node(Guid Id, Guid? Parent, JsonNode Body) : ISnapshotTreeNode;

    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000000");

    private static Node N(Guid id, string body, Guid? parent = null) =>
        new(id, parent, JsonNode.Parse(body)!);

    /// <summary>An extra comparison that never finds a difference.</summary>
    private static ExtraComparison<string> NoExtra(Node d, Node l, bool bodyDiffers) =>
        new("none", []);

    /// <summary>Classifies with the identity normaliser and <see cref="NoExtra"/> by default.</summary>
    private static TreeDiff<Node, string> Classify(
        Node[] desired,
        Node[] live,
        Func<JsonNode, JsonNode>? normalise = null,
        Func<Node, Node, bool, ExtraComparison<string>>? extra = null
    ) => SnapshotTreeDiff.Classify(desired, live, normalise ?? (b => b), extra ?? NoExtra);

    [Fact]
    public void Classify_SnapshotItemAbsentLive_IsAdded()
    {
        // Arrange
        var desired = new[] { N(A, """{"x":1}""", parent: B) };

        // Act
        var entry = Assert.Single(Classify(desired, []).Entries);

        // Assert
        Assert.Equal(
            (TreeChangeKind.Added, A, (Guid?)B),
            (entry.Kind, entry.Node.Id, entry.Node.Parent)
        );
    }

    [Fact]
    public void Classify_LiveItemAbsentFromSnapshot_IsRemovedWithTheLiveNode()
    {
        // Arrange
        var live = N(A, """{"x":1}""");

        // Act
        var entry = Assert.Single(Classify([], [live]).Entries);

        // Assert
        Assert.Equal((TreeChangeKind.Removed, live), (entry.Kind, entry.Node));
    }

    [Fact]
    public void Classify_SameBodyAndParent_CountsUnchanged()
    {
        // Arrange
        Node[] items = [N(A, """{"x":1}""")];

        // Act
        var diff = Classify(items, [N(A, """{"x":1}""")]);

        // Assert
        Assert.Equal((0, 1), (diff.Entries.Count, diff.Unchanged));
    }

    [Fact]
    public void Classify_BodyDiffers_IsChangedWithBodyPaths()
    {
        // Act
        var entry = Assert.Single(Classify([N(A, """{"x":1}""")], [N(A, """{"x":2}""")]).Entries);

        // Assert
        Assert.Equal(
            (TreeChangeKind.Changed, true, "x"),
            (entry.Kind, entry.BodyChanged, string.Join(",", entry.Changes!))
        );
    }

    [Fact]
    public void Classify_BodyDiffersOnlyInWhatTheNormaliserDrops_IsUnchanged()
    {
        // Arrange: the normaliser drops "noise", so only a noise difference compares equal.
        static JsonNode DropNoise(JsonNode b)
        {
            var clone = b.DeepClone().AsObject();
            clone.Remove("noise");
            return clone;
        }

        // Act
        var diff = Classify(
            [N(A, """{"x":1,"noise":1}""")],
            [N(A, """{"x":1,"noise":2}""")],
            normalise: DropNoise
        );

        // Assert
        Assert.Equal(1, diff.Unchanged);
    }

    [Fact]
    public void Classify_OnlyExtraDiffers_IsChangedWithExtraPathsAndValue()
    {
        // Arrange
        ExtraComparison<string> Extra(Node d, Node l, bool bodyDiffers) => new("file", ["file"]);

        // Act
        var entry = Assert.Single(
            Classify([N(A, """{"x":1}""")], [N(A, """{"x":1}""")], extra: Extra).Entries
        );

        // Assert
        Assert.Equal(
            (TreeChangeKind.Changed, false, "file", "file"),
            (entry.Kind, entry.BodyChanged, entry.Extra, string.Join(",", entry.Changes!))
        );
    }

    [Fact]
    public void Classify_BodyAndExtraDiffer_ListsBodyPathsBeforeExtraPaths()
    {
        // Arrange
        ExtraComparison<string> Extra(Node d, Node l, bool bodyDiffers) => new("s", ["state"]);

        // Act
        var entry = Assert.Single(
            Classify([N(A, """{"x":1}""")], [N(A, """{"x":2}""")], extra: Extra).Entries
        );

        // Assert
        Assert.Equal(["x", "state"], entry.Changes!);
    }

    [Fact]
    public void Classify_ExtraHook_IsToldWhetherTheBodyDiffers()
    {
        // Arrange
        var seen = new List<bool>();
        ExtraComparison<string> Extra(Node d, Node l, bool bodyDiffers)
        {
            seen.Add(bodyDiffers);
            return new("", []);
        }

        // Act
        Classify(
            [N(A, """{"x":1}"""), N(B, """{"x":1}""")],
            [N(A, """{"x":2}"""), N(B, """{"x":1}""")],
            extra: Extra
        );

        // Assert
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void Classify_OnlyParentDiffers_IsDriftedWithParentPath()
    {
        // Act
        var entry = Assert.Single(
            Classify([N(A, """{"x":1}""", parent: B)], [N(A, """{"x":1}""", parent: C)]).Entries
        );

        // Assert
        Assert.Equal(
            (TreeChangeKind.Drifted, "parent"),
            (entry.Kind, string.Join(",", entry.Changes!))
        );
    }

    [Fact]
    public void Classify_BodyAndParentDiffer_IsChangedNotDrifted()
    {
        // Act
        var entry = Assert.Single(
            Classify([N(A, """{"x":1}""", parent: B)], [N(A, """{"x":2}""", parent: C)]).Entries
        );

        // Assert
        Assert.Equal(TreeChangeKind.Changed, entry.Kind);
    }

    [Fact]
    public void Classify_Entries_AreSnapshotOrderThenRemovedInLiveOrder()
    {
        // Arrange: C and A are live only (removed), B is added.
        var live = new[] { N(C, "{}"), N(A, "{}") };

        // Act
        var diff = Classify([N(B, "{}")], live);

        // Assert
        Assert.Equal([B, C, A], diff.Entries.Select(e => e.Node.Id));
    }

    [Fact]
    public void Classify_LiveParents_MapsEveryLiveItemToItsParent()
    {
        // Arrange
        var live = new[] { N(A, "{}"), N(B, "{}", parent: A) };

        // Act
        var parents = Classify([N(A, "{}")], live).LiveParents();

        // Assert
        Assert.Equal(new Dictionary<Guid, Guid?> { [A] = null, [B] = A }, parents);
    }

    [Fact]
    public void Classify_DuplicateLiveId_LastLiveItemWins()
    {
        // Arrange: a hand-edited snapshot repeats A; the later copy matches the snapshot.
        var live = new[] { N(A, """{"x":2}"""), N(A, """{"x":1}""") };

        // Act
        var diff = Classify([N(A, """{"x":1}""")], live);

        // Assert
        Assert.Equal(1, diff.Unchanged);
    }
}
