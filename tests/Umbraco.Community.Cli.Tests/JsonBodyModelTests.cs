using Umbraco.Community.Cli.Client;
using Umbraco.Community.Cli.Commands;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Tests;

/// <summary>
/// A wrong-typed <c>--json-body</c> field is reported by its path and the shape it takes, in the
/// caller's terms, never with a CLI-internal .NET type name (#459).
/// </summary>
public class JsonBodyModelTests
{
    private const string Hint = " Run with --schema to see the body's shape.";

    [Fact]
    public void Deserialize_ValidBody_ReadsTheRequest()
    {
        var request = JsonBodyModel.Deserialize<CreateContentRequest>(
            """{ "contentType": { "alias": "blogPost" }, "variants": [{ "name": "Post" }] }"""
        )!;

        Assert.Equal(
            ("blogPost", "Post"),
            (request.ContentType.Alias, request.Variants.Single().Name)
        );
    }

    [Fact]
    public void Deserialize_StringContentType_SaysItMustBeAReferenceObject()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<CreateContentRequest>(
                """{ "contentType": "probe", "variants": [{ "name": "P1" }] }"""
            )
        );

        Assert.Equal(
            """--json-body contentType must be an object such as {"alias": "blogPost"} or {"id": "<guid>"}, not "probe"."""
                + Hint,
            ex.Message
        );
    }

    [Fact]
    public void Deserialize_WrongTypeInsideAnArrayItem_NamesTheItemsPath()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<UpdateContentRequest>("""{ "variants": [{ "name": 5 }] }""")
        );

        Assert.Equal(
            "--json-body variants[0].name must be a string, not a number." + Hint,
            ex.Message
        );
    }

    [Fact]
    public void Deserialize_ObjectWhereAnArrayIsExpected_DescribesTheItems()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<UpdateMediaRequest>("""{ "variants": { "name": "x" } }""")
        );

        Assert.Equal(
            "--json-body variants must be an array, each item an object with the keys culture, "
                + "segment and name, not an object."
                + Hint,
            ex.Message
        );
    }

    [Fact]
    public void Deserialize_IdThatIsNotAGuid_SaysAnIdIsExpected()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<CreateContentRequest>(
                """{ "contentType": { "id": "probe" } }"""
            )
        );

        Assert.Equal(
            """--json-body contentType.id must be an id, as a string such as "3f7a8b2e-1234-5678-abcd-ef0123456789", not "probe"."""
                + Hint,
            ex.Message
        );
    }

    [Fact]
    public void Deserialize_MissingRequiredKey_NamesTheKeyAndItsShape()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<CreateDocumentBlueprintRequest>("""{ "variants": [] }""")
        );

        Assert.Equal(
            """--json-body needs documentType, an object such as {"alias": "blogPost"} or {"id": "<guid>"}."""
                + Hint,
            ex.Message
        );
    }

    [Fact]
    public void Deserialize_ArrayBody_SaysTheBodyMustBeAnObject()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<UpdateDocumentBlueprintRequest>("[1]")
        );

        Assert.Equal(
            "--json-body as a whole must be an object with the keys values and variants, not an array."
                + Hint,
            ex.Message
        );
    }

    /// <summary>The acceptance criterion itself, over every typed body and a spread of mismatches.</summary>
    [Theory]
    [InlineData("""{ "contentType": "probe" }""")]
    [InlineData("""{ "contentType": { "alias": "p" }, "parent": "root" }""")]
    [InlineData("""{ "contentType": { "alias": "p" }, "template": 5 }""")]
    [InlineData("""{ "contentType": { "alias": "p" }, "values": [ "title" ] }""")]
    [InlineData("""{ "contentType": { "alias": "p" }, "variants": [{ "culture": true }] }""")]
    [InlineData("""{ "id": 12, "contentType": { "alias": "p" } }""")]
    public void Deserialize_AnyMismatch_NamesNoDotNetType(string body)
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<CreateContentRequest>(body)
        );

        Assert.DoesNotMatch(@"Umbraco\.|System\.", ex.Message);
    }
}
