using System.Text.Json.Nodes;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The <c>changes</c> paths of a diff row (#229): named by key and by Umbraco item identity, and
/// empty exactly when the bodies are equal.
/// </summary>
public class JsonPathDiffTests
{
    private static IReadOnlyList<string> Paths(string desired, string current) =>
        JsonPathDiff.Paths(JsonNode.Parse(desired), JsonNode.Parse(current));

    [Fact]
    public void Paths_EqualBodies_IsEmpty()
    {
        Assert.Empty(Paths("""{"a":1,"b":[1,2]}""", """{"b":[1,2],"a":1}"""));
    }

    [Fact]
    public void Paths_NestedScalarChange_IsDotted()
    {
        Assert.Equal(
            ["template.id"],
            Paths("""{"template":{"id":"x"}}""", """{"template":{"id":"y"}}""")
        );
    }

    [Fact]
    public void Paths_MissingKey_IsReported()
    {
        Assert.Equal(["icon"], Paths("""{"icon":"i"}""", "{}"));
    }

    [Fact]
    public void Paths_PropertyValue_IsNamedByAliasAndCultureAndNotEntered()
    {
        var paths = Paths(
            """{"values":[{"alias":"title","culture":"en-US","value":{"deep":1}}]}""",
            """{"values":[{"alias":"title","culture":"en-US","value":{"deep":2}}]}"""
        );

        Assert.Equal(["values.title[en-US]"], paths);
    }

    [Fact]
    public void Paths_Variant_IsNamedByCulture()
    {
        var paths = Paths(
            """{"variants":[{"culture":"en-US","name":"Home"},{"culture":null,"name":"X"}]}""",
            """{"variants":[{"culture":"en-US","name":"Start"},{"culture":null,"name":"X"}]}"""
        );

        Assert.Equal(["variants[en-US].name"], paths);
    }

    [Fact]
    public void Paths_InsertedAliasedItem_DoesNotReportTheOthers()
    {
        var paths = Paths(
            """{"properties":[{"alias":"new"},{"alias":"a","mandatory":true}]}""",
            """{"properties":[{"alias":"a","mandatory":true}]}"""
        );

        Assert.Equal(["properties.new"], paths);
    }

    [Fact]
    public void Paths_ReorderedAliasedItems_ReportsTheArray()
    {
        var paths = Paths(
            """{"properties":[{"alias":"a"},{"alias":"b"}]}""",
            """{"properties":[{"alias":"b"},{"alias":"a"}]}"""
        );

        Assert.Equal(["properties"], paths);
    }

    [Fact]
    public void Paths_UnkeyedArrayOfSameLength_IsMatchedByIndex()
    {
        Assert.Equal(
            ["containers[1].name"],
            Paths(
                """{"containers":[{"name":"A"},{"name":"B"}]}""",
                """{"containers":[{"name":"A"},{"name":"C"}]}"""
            )
        );
    }

    [Fact]
    public void Paths_UnkeyedArrayOfNewLength_IsReportedWhole()
    {
        Assert.Equal(["tags"], Paths("""{"tags":[1]}""", """{"tags":[1,2]}"""));
    }
}
