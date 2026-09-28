using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The schema kind table (#273): every snapshot section and every diff part is declared by exactly
/// one kind, and the create and delete orders come from the same stages, so they cannot drift.
/// </summary>
public class SchemaKindsTests
{
    /// <summary>The snapshot's section members, read off its JSON attributes.</summary>
    private static readonly PropertyInfo[] SnapshotSections =
    [
        .. typeof(SchemaSnapshot)
            .GetProperties()
            .Where(p =>
                p.PropertyType == typeof(List<JsonNode>)
                && p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null
            ),
    ];

    [Fact]
    public void All_CoversEverySnapshotSectionOnce()
    {
        // Arrange
        var sections = SnapshotSections
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)
            .Order();

        // Act
        var members = SchemaKinds.All.Select(k => k.Member).Order();

        // Assert
        Assert.Equal(sections, members);
    }

    [Fact]
    public void Section_ReadsTheSnapshotPropertyItsMemberNames()
    {
        // Arrange: a snapshot whose every section holds one entry naming its own member.
        var snapshot = new SchemaSnapshot();
        foreach (var section in SnapshotSections)
        {
            var member = section.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;
            section.SetValue(snapshot, new List<JsonNode> { JsonValue.Create(member)! });
        }

        // Act
        var read = SchemaKinds.All.Select(k => (k.Member, (string?)k.Section(snapshot)?.Single()));

        // Assert
        Assert.All(read, r => Assert.Equal(r.Member, r.Item2));
    }

    [Fact]
    public void Diff_EveryDiffPartBelongsToOneKind()
    {
        // Arrange: a diff whose every part is a distinct instance.
        var parts = typeof(SchemaDiff)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(SchemaKindDiff))
            .ToList();
        var diff = SchemaDiff.Empty;
        foreach (var kind in SchemaKinds.All)
            diff = kind.WithDiff(diff, new SchemaKindDiff([], [], [], [], 0));

        // Act
        var distinct = SchemaKinds
            .All.Select(k => k.Diff(diff))
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count();

        // Assert
        Assert.Equal(parts.Count, distinct);
    }

    [Fact]
    public void DeleteOrder_EntityKinds_AreTheCreateOrderReversed()
    {
        // Arrange
        var creates = SchemaKinds.CreateOrder.Where(k => k.File is null).Select(k => k.Tag);

        // Act
        var deletes = SchemaKinds.DeleteOrder.Where(k => k.File is null).Select(k => k.Tag);

        // Assert
        Assert.Equal(creates.Reverse(), deletes);
    }

    [Fact]
    public void CreateOrder_StaticFilesComeFirst_AndDeleteOrderPutsThemLast()
    {
        // Arrange
        var files = SchemaKinds.All.Count(k => k.File is not null);

        // Act
        var firstCreates = SchemaKinds.CreateOrder.Take(files).All(k => k.File is not null);
        var lastDeletes = SchemaKinds.DeleteOrder.TakeLast(files).All(k => k.File is not null);

        // Assert
        Assert.True(firstCreates && lastDeletes);
    }

    [Fact]
    public void ApplyStage_IsUniquePerKind()
    {
        // Act
        var stages = SchemaKinds.All.Select(k => k.ApplyStage).Distinct().Count();

        // Assert
        Assert.Equal(SchemaKinds.All.Count, stages);
    }

    [Fact]
    public void Of_KnownTag_ReturnsItsSpec()
    {
        // Act
        var spec = SchemaKinds.Of(SchemaKinds.DataType);

        // Assert
        Assert.Equal("name", spec.KeyField);
    }

    [Fact]
    public void Of_UnknownTag_Throws()
    {
        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SchemaKinds.Of("contentType"));
    }
}
