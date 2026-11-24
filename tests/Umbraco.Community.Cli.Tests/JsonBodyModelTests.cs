using System.Text.Json.Nodes;
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

    // ── Unknown keys (#474) ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_UnknownKeysAtAnyDepth_ListsEveryPathInDocumentOrder()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<UpdateContentRequest>(
                """{ "valuez": [], "variants": [{ "nmae": "x" }], "template": { "alais": "home" } }""",
                allowUnknownKeys: false,
                "the content update schema",
                typeof(ContentItemResponse)
            )
        );

        Assert.Contains(
            "does not define: $.valuez, $.variants[0].nmae, $.template.alais.",
            ex.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Deserialize_UnknownKeyAllowed_ReadsTheRestAndDropsIt()
    {
        var request = JsonBodyModel.Deserialize<UpdateContentRequest>(
            """{ "valuez": [], "variants": [{ "name": "Kept", "nmae": "x" }] }""",
            allowUnknownKeys: true,
            "the content update schema",
            typeof(ContentItemResponse)
        )!;

        Assert.Equal("Kept", request.Variants.Single().Name);
    }

    [Fact]
    public void Deserialize_ValueOfAnyShape_IsNotChecked()
    {
        // A block editor's value has whatever keys its editor takes.
        var request = JsonBodyModel.Deserialize<UpdateMediaRequest>(
            """{ "values": [{ "alias": "blocks", "value": { "layout": {}, "anything": [1] } }] }""",
            allowUnknownKeys: false,
            "the media update schema",
            typeof(MediaItemResponse)
        )!;

        Assert.Equal("blocks", request.Values.Single().Alias);
    }

    [Fact]
    public void Deserialize_ReadOnlyKeyOfGetAtTheWrongPosition_IsRejected()
    {
        // "urls" is a top-level key of content get, not a variant key.
        var ex = Assert.Throws<InvalidInputException>(() =>
            JsonBodyModel.Deserialize<UpdateContentRequest>(
                """{ "variants": [{ "name": "x", "urls": [] }] }""",
                allowUnknownKeys: false,
                "the content update schema",
                typeof(ContentItemResponse)
            )
        );

        Assert.Contains("$.variants[0].urls", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_FullMediaGetOutput_IsAValidMediaUpdateBody()
    {
        // Every key media get can print, nulls included, as its output carries them.
        var get = System.Text.Json.JsonSerializer.Serialize(
            new MediaItemResponse
            {
                Id = Guid.NewGuid(),
                Name = "Logo",
                MediaType = new ContentTypeRef
                {
                    Id = Guid.NewGuid(),
                    Alias = "image",
                    Icon = "icon-picture",
                    Collection = new ContentParentReference { Id = Guid.NewGuid() },
                },
                Parent = new ContentParentReference { Id = Guid.NewGuid() },
                UpdateDate = DateTimeOffset.UnixEpoch,
                Values =
                [
                    new ContentValueResponse
                    {
                        Alias = "umbracoFile",
                        EditorAlias = "Umbraco.ImageCropper",
                        Value = JsonNode.Parse("""{ "src": "/media/logo.png", "crops": [] }"""),
                    },
                ],
                Urls = [new UrlInfo { Culture = null, Url = "/media/logo.png" }],
            }
        );

        var request = JsonBodyModel.Deserialize<UpdateMediaRequest>(
            get,
            allowUnknownKeys: false,
            "the media update schema",
            typeof(MediaItemResponse)
        )!;

        Assert.Equal("umbracoFile", request.Values.Single().Alias);
    }

    /// <summary>
    /// <c>document-blueprint get</c> and <c>scaffold</c> print the Management API's
    /// <c>DocumentBlueprintResponseModel</c> verbatim, and the docs pipe them into
    /// <c>content create</c> and back into the blueprint verbs. A body with every key that model
    /// has, at every depth, built from the spec, must pass each of those checks.
    /// </summary>
    [Fact]
    public void CheckKeys_EveryKeyOfTheApisBlueprintShape_PassesTheDocumentBodies()
    {
        // Arrange
        var schemas = JsonNode.Parse(
            File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "spec", "management.json"))
        )!["components"]!["schemas"]!.AsObject();
        var body = Instance(schemas, schemas["DocumentBlueprintResponseModel"]!)!;

        // Act
        var failures = new (string Verb, Action Check)[]
        {
            (
                "content create",
                () =>
                    JsonBodyModel.CheckKeys<CreateContentRequest>(
                        body,
                        "s",
                        typeof(ContentItemResponse)
                    )
            ),
            (
                "document-blueprint create",
                () =>
                    JsonBodyModel.CheckKeys<CreateDocumentBlueprintRequest>(
                        body,
                        "s",
                        typeof(ContentItemResponse)
                    )
            ),
            (
                "document-blueprint update",
                () =>
                    JsonBodyModel.CheckKeys<UpdateDocumentBlueprintRequest>(
                        body,
                        "s",
                        typeof(ContentItemResponse)
                    )
            ),
        }
            .Select(c => (c.Verb, Error: Record.Exception(c.Check)?.Message))
            .Where(r => r.Error is not null)
            .ToList();

        // Assert
        Assert.Empty(failures);
    }

    /// <summary>
    /// Builds a JSON value with every property a spec component defines, recursively: each object
    /// gets all its keys, each array one item, each scalar a string.
    /// </summary>
    private static JsonNode? Instance(JsonObject schemas, JsonNode schema)
    {
        var reference =
            schema["$ref"]?.GetValue<string>()
            ?? schema["oneOf"]
                ?.AsArray()
                .Select(o => o!["$ref"]?.GetValue<string>())
                .FirstOrDefault(r => r is not null)
            ?? schema["allOf"]
                ?.AsArray()
                .Select(o => o!["$ref"]?.GetValue<string>())
                .FirstOrDefault(r => r is not null);
        if (reference is not null)
            return Instance(schemas, schemas[reference.Split('/')[^1]]!);
        if (schema["properties"] is JsonObject properties)
            return new JsonObject(
                properties.Select(p => KeyValuePair.Create(p.Key, Instance(schemas, p.Value!)))
            );
        if (schema["type"]?.GetValue<string>() == "array")
            return new JsonArray(Instance(schemas, schema["items"]!));
        return "x";
    }
}
