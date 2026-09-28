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

    // Found live (#198): a hand-written property without "description" stayed "Changed" against
    // the live "description": null, so every apply re-planned an update.
    [Fact]
    public void Paths_AliasedItemMissingANullMember_IsEmpty()
    {
        var paths = Paths(
            """{"properties":[{"alias":"a","name":"A"}]}""",
            """{"properties":[{"alias":"a","name":"A","description":null}]}"""
        );

        Assert.Empty(paths);
    }

    // #350: Umbraco carries order in sortOrder, not array position, and returns items in its own
    // order, so a hand-written snapshot in another order must still compare equal.
    [Fact]
    public void Paths_ReorderedAliasedItemsMissingANullMember_IsEmpty()
    {
        var paths = Paths(
            """{"properties":[{"alias":"a"},{"alias":"b"}]}""",
            """{"properties":[{"alias":"b","description":null},{"alias":"a"}]}"""
        );

        Assert.Empty(paths);
    }

    [Fact]
    public void Paths_ReorderedAliasedItems_IsEmpty()
    {
        var paths = Paths(
            """{"properties":[{"alias":"a"},{"alias":"b"}]}""",
            """{"properties":[{"alias":"b"},{"alias":"a"}]}"""
        );

        Assert.Empty(paths);
    }

    [Fact]
    public void Paths_ReorderedItemsWithIds_IsEmpty()
    {
        var paths = Paths(
            """{"containers":[{"id":"c1","name":"Content"},{"id":"c2","name":"Settings"}]}""",
            """{"containers":[{"id":"c2","name":"Settings"},{"id":"c1","name":"Content"}]}"""
        );

        Assert.Empty(paths);
    }

    [Fact]
    public void Paths_ReorderedItemsWithIdsAndAChange_NamesTheItemById()
    {
        var paths = Paths(
            """{"containers":[{"id":"c1","sortOrder":1},{"id":"c2","sortOrder":0}]}""",
            """{"containers":[{"id":"c2","sortOrder":0},{"id":"c1","sortOrder":0}]}"""
        );

        Assert.Equal(["containers[c1].sortOrder"], paths);
    }

    [Fact]
    public void Paths_ReorderedAllowedDocumentTypes_IsEmpty()
    {
        var paths = Paths(
            """{"allowedDocumentTypes":[{"documentType":{"id":"a"},"sortOrder":0},{"documentType":{"id":"b"},"sortOrder":1}]}""",
            """{"allowedDocumentTypes":[{"documentType":{"id":"b"},"sortOrder":1},{"documentType":{"id":"a"},"sortOrder":0}]}"""
        );

        Assert.Empty(paths);
    }

    [Fact]
    public void Paths_ReorderedCompositionsWithAChange_NamesTheItemByItsReference()
    {
        var paths = Paths(
            """{"compositions":[{"documentType":{"id":"a"},"compositionType":"Composition"},{"documentType":{"id":"b"},"compositionType":"Inheritance"}]}""",
            """{"compositions":[{"documentType":{"id":"b"},"compositionType":"Composition"},{"documentType":{"id":"a"},"compositionType":"Composition"}]}"""
        );

        Assert.Equal(["compositions[b].compositionType"], paths);
    }

    [Fact]
    public void Paths_GuidsDifferingOnlyInCase_IsEmpty()
    {
        var paths = Paths(
            """{"containers":[{"id":"3F2A0000-0000-0000-0000-00000000000A","parent":{"id":"3F2A0000-0000-0000-0000-00000000000B"}}]}""",
            """{"containers":[{"id":"3f2a0000-0000-0000-0000-00000000000a","parent":{"id":"3f2a0000-0000-0000-0000-00000000000b"}}]}"""
        );

        Assert.Empty(paths);
    }

    [Fact]
    public void Paths_DifferentGuids_StillDiffer()
    {
        var paths = Paths(
            """{"template":{"id":"3F2A0000-0000-0000-0000-00000000000A"}}""",
            """{"template":{"id":"3f2a0000-0000-0000-0000-00000000000b"}}"""
        );

        Assert.Equal(["template.id"], paths);
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
