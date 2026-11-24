using System.Text.Json.Nodes;
using Umbraco.Community.Cli.Client;
using Umbraco.Community.Cli.Commands;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Tests;

/// <summary>
/// The JSON Schemas <c>--schema</c> prints for the raw schema verbs come from the Management API
/// spec the client is generated from, never a hand-written copy. They are committed as
/// <c>src/Umbraco.Community.Cli/Schemas/api-bodies.json</c> (embedded in the CLI, so <c>--schema</c> needs no
/// host) and this test regenerates them from <c>spec/management.json</c>, failing on any drift. To
/// accept a spec update, rerun with <c>UPDATE_API_SCHEMAS=1</c>.
/// </summary>
public class ApiBodySchemaTests
{
    /// <summary>The request models <c>--json-body</c> takes, and whether each is an update.</summary>
    private static readonly (string Model, bool Update)[] Models =
    [
        ("CreateDocumentTypeRequestModel", false),
        ("UpdateDocumentTypeRequestModel", true),
        ("CreateDataTypeRequestModel", false),
        ("UpdateDataTypeRequestModel", true),
        ("CreateMediaTypeRequestModel", false),
        ("UpdateMediaTypeRequestModel", true),
        ("CreateMemberTypeRequestModel", false),
        ("UpdateMemberTypeRequestModel", true),
        ("UpdateTemplateRequestModel", true),
    ];

    [Fact]
    public void EmbeddedSchemas_MatchTheSpec()
    {
        // Arrange
        var root = TestPaths.RepoRoot();
        var path = Path.Combine(root, "src", "Umbraco.Community.Cli", "Schemas", "api-bodies.json");
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "spec", "management.json")))!;
        // Indented JSON uses the platform newline; the committed file is LF.
        var actual =
            Generate(spec["components"]!["schemas"]!.AsObject())
                .ToJsonString(Indented)
                .Replace("\r\n", "\n") + "\n";

        if (Environment.GetEnvironmentVariable("UPDATE_API_SCHEMAS") == "1")
            File.WriteAllText(path, actual);

        // Act
        var committed = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";

        // Assert
        Assert.True(
            committed == actual,
            "src/Umbraco.Community.Cli/Schemas/api-bodies.json is out of date with spec/management.json. "
                + "Run `UPDATE_API_SCHEMAS=1 dotnet test --project tests/Umbraco.Community.Cli.Tests "
                + "--filter-class *ApiBodySchemaTests` and commit it."
        );
    }

    [Theory]
    [InlineData(EntityKind.DocumentType, false, "CreateDocumentTypeRequestModel")]
    [InlineData(EntityKind.DataType, true, "UpdateDataTypeRequestModel")]
    [InlineData(EntityKind.Template, true, "UpdateTemplateRequestModel")]
    public void For_Kind_IsAJsonSchemaDocumentForItsRequestModel(
        EntityKind kind,
        bool update,
        string model
    )
    {
        var schema = ApiBodySchema.For(kind, update);

        Assert.Equal(
            (model, $"#/$defs/{model}"),
            (schema["title"]!.GetValue<string>(), schema["$ref"]!.GetValue<string>())
        );
    }

    [Fact]
    public void For_Update_RequiresNothing()
    {
        // update --json-body merges, so any subset of the keys is a valid body.
        var model = ApiBodySchema.For(EntityKind.DocumentType, update: true)["$defs"]![
            "UpdateDocumentTypeRequestModel"
        ]!;

        Assert.Null(model["required"]);
    }

    [Fact]
    public void For_KindWithNoBody_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApiBodySchema.For(EntityKind.Template, update: false)
        );
    }

    // ── Unknown keys (#460) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{ "allowedTemplatez": [] }""", "$.allowedTemplatez")]
    [InlineData(
        """{ "allowedTemplates": [{ "template": { "id": "c17eef11-0000-0000-0000-000000000000" } }] }""",
        "$.allowedTemplates[0].template"
    )]
    [InlineData(
        """{ "properties": [{ "alias": "title", "validation": { "mandatory": true, "required": true } }] }""",
        "$.properties[0].validation.required"
    )]
    [InlineData(
        """{ "collection": { "id": "c17eef11-0000-0000-0000-000000000000", "alias": "list" } }""",
        "$.collection.alias"
    )]
    [InlineData("""{ "odd key": 1 }""", "$['odd key']")]
    public void FindUnknownKeys_DocumentTypeUpdateWithAnUnknownKey_ReturnsItsPath(
        string body,
        string path
    )
    {
        var unknown = ApiBodySchema.FindUnknownKeys(
            EntityKind.DocumentType,
            update: true,
            JsonNode.Parse(body)!
        );

        Assert.Equal([path], unknown);
    }

    [Fact]
    public void FindUnknownKeys_SeveralUnknownKeys_ReturnsEveryPathInDocumentOrder()
    {
        var body = JsonNode.Parse(
            """{ "nmae": "x", "values": [{ "alias": "a", "value": 1, "vlaue": 2 }] }"""
        )!;

        var unknown = ApiBodySchema.FindUnknownKeys(EntityKind.DataType, update: true, body);

        Assert.Equal(["$.nmae", "$.values[0].vlaue"], unknown);
    }

    [Fact]
    public void FindUnknownKeys_KnownKeysAndNulls_ReturnsNothing()
    {
        var body = JsonNode.Parse(
            """
            { "name": "Blog post", "collection": null, "allowedTemplates": [{ "id": "c17eef11-0000-0000-0000-000000000000" }],
              "cleanup": { "preventCleanup": false, "keepAllVersionsNewerThanDays": null } }
            """
        )!;

        var unknown = ApiBodySchema.FindUnknownKeys(EntityKind.DocumentType, update: true, body);

        Assert.Empty(unknown);
    }

    [Fact]
    public void FindUnknownKeys_DataTypeValueOfAnyShape_IsNotChecked()
    {
        // An editor's configuration value is free-form in the spec ("value": {}), so its keys are
        // whatever the editor takes.
        var body = JsonNode.Parse(
            """{ "values": [{ "alias": "blocks", "value": [{ "contentElementTypeKey": "x", "anything": true }] }] }"""
        )!;

        var unknown = ApiBodySchema.FindUnknownKeys(EntityKind.DataType, update: true, body);

        Assert.Empty(unknown);
    }

    [Fact]
    public void FindUnknownKeys_ReadOnlyKeyFromGetAtTheTopLevel_IsAccepted()
    {
        var body = JsonNode.Parse(
            """{ "id": "c17eef11-0000-0000-0000-000000000000", "isDeletable": true, "canIgnoreStartNodes": false }"""
        )!;

        var unknown = ApiBodySchema.FindUnknownKeys(EntityKind.DataType, update: true, body);

        Assert.Empty(unknown);
    }

    [Fact]
    public void FindUnknownKeys_ReadOnlyKeyNameBelowTheTopLevel_IsRejected()
    {
        var body = JsonNode.Parse("""{ "values": [{ "alias": "a", "isDeletable": true }] }""")!;

        var unknown = ApiBodySchema.FindUnknownKeys(EntityKind.DataType, update: true, body);

        Assert.Equal(["$.values[0].isDeletable"], unknown);
    }

    /// <summary>
    /// The hands-on fixture is a schema export from a real Umbraco 17 site: every item in it is the
    /// verbatim <c>get</c> body. Each must pass both its update and (where there is one) its create
    /// check, or editing <c>get</c> output and writing it back would break.
    /// </summary>
    [Theory]
    [InlineData("documentTypes", EntityKind.DocumentType, true)]
    [InlineData("documentTypes", EntityKind.DocumentType, false)]
    [InlineData("mediaTypes", EntityKind.MediaType, true)]
    [InlineData("mediaTypes", EntityKind.MediaType, false)]
    [InlineData("memberTypes", EntityKind.MemberType, true)]
    [InlineData("memberTypes", EntityKind.MemberType, false)]
    [InlineData("dataTypes", EntityKind.DataType, true)]
    [InlineData("dataTypes", EntityKind.DataType, false)]
    [InlineData("templates", EntityKind.Template, true)]
    public void FindUnknownKeys_RealGetBodies_ReturnsNothing(
        string section,
        EntityKind kind,
        bool update
    )
    {
        // Arrange
        var fixture = JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(TestPaths.RepoRoot(), "tests", "hands-on", "fixtures", "schema.json")
            )
        )!;
        var items = fixture[section]!.AsArray();

        // Act
        var unknown = items.SelectMany(item => ApiBodySchema.FindUnknownKeys(kind, update, item!));

        // Assert
        Assert.NotEmpty(items);
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData(EntityKind.DocumentType)]
    [InlineData(EntityKind.MediaType)]
    [InlineData(EntityKind.MemberType)]
    [InlineData(EntityKind.DataType)]
    public void FindUnknownKeys_MinimalExampleBody_ReturnsNothing(EntityKind kind)
    {
        // --example prints this on a site with no items of the kind; it must create as it is.
        var body = RawBodyCommand.MinimalBody(kind)!;

        Assert.Empty(ApiBodySchema.FindUnknownKeys(kind, update: false, body));
    }

    [Theory]
    [InlineData(
        EntityKind.DocumentType,
        "DocumentTypeResponseModel",
        "UpdateDocumentTypeRequestModel"
    )]
    [InlineData(EntityKind.MediaType, "MediaTypeResponseModel", "UpdateMediaTypeRequestModel")]
    [InlineData(EntityKind.MemberType, "MemberTypeResponseModel", "UpdateMemberTypeRequestModel")]
    [InlineData(EntityKind.DataType, "DataTypeResponseModel", "UpdateDataTypeRequestModel")]
    [InlineData(EntityKind.Template, "TemplateResponseModel", "UpdateTemplateRequestModel")]
    public void ReadOnlyKeys_Kind_AreTheResponseModelKeysTheUpdateModelLacks(
        EntityKind kind,
        string response,
        string update
    )
    {
        // Arrange
        var schemas = JsonNode.Parse(
            File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "spec", "management.json"))
        )!["components"]!["schemas"]!;
        var updateKeys = schemas[update]!["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
        var expected = schemas[response]!["properties"]!
            .AsObject()
            .Select(p => p.Key)
            .Where(k => !updateKeys.Contains(k))
            .Order(StringComparer.Ordinal);

        // Act
        var actual = ApiBodySchema.ReadOnlyKeys(kind).Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(expected, actual);
    }

    // ── Generation (OpenAPI 3.0 component -> JSON Schema 2020-12 document) ────────────

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds every model's document, keyed by model name.</summary>
    private static JsonObject Generate(JsonObject components)
    {
        var documents = new JsonObject();
        foreach (var (model, update) in Models)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            Collect(components, model, names);
            var defs = new JsonObject();
            foreach (var name in names)
                defs[name] = Convert(components[name]!.DeepClone());
            // update merges into the current item, so none of its keys is required.
            if (update)
                defs[model]!.AsObject().Remove("required");
            documents[model] = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["title"] = model,
                ["$ref"] = $"#/$defs/{model}",
                ["$defs"] = defs,
            };
        }
        return documents;
    }

    /// <summary>Adds <paramref name="name"/> and every component it references to <paramref name="names"/>.</summary>
    private static void Collect(JsonObject components, string name, ISet<string> names)
    {
        if (!names.Add(name))
            return;
        foreach (var r in Refs(components[name]!))
            Collect(components, r, names);
    }

    private static IEnumerable<string> Refs(JsonNode node) =>
        node switch
        {
            JsonObject o => o.SelectMany(p =>
                p.Key == "$ref" && p.Value!.GetValue<string>() is var r
                    ? [r[(r.LastIndexOf('/') + 1)..]]
                : p.Value is null ? []
                : Refs(p.Value)
            ),
            JsonArray a => a.Where(i => i is not null).SelectMany(i => Refs(i!)),
            _ => [],
        };

    /// <summary>
    /// Rewrites one OpenAPI 3.0 schema as JSON Schema: <c>#/components/schemas/X</c> becomes
    /// <c>#/$defs/X</c>, and <c>nullable: true</c> becomes an allowed <c>null</c>.
    /// </summary>
    private static JsonNode Convert(JsonNode node)
    {
        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                if (array[i] is { } item)
                    array[i] = Convert(item.DeepClone());
            return array;
        }
        if (node is not JsonObject obj)
            return node;

        foreach (var key in obj.Select(p => p.Key).ToList())
            if (obj[key] is { } child)
                obj[key] =
                    key == "$ref"
                        ? "#/$defs/" + child.GetValue<string>().Split('/')[^1]
                        : Convert(child.DeepClone());

        if (obj["nullable"]?.GetValue<bool>() == true)
        {
            obj.Remove("nullable");
            if (obj["type"] is JsonValue type)
                obj["type"] = new JsonArray(type.GetValue<string>(), "null");
            else if (obj["oneOf"] is JsonArray oneOf)
                oneOf.Add(new JsonObject { ["type"] = "null" });
            else if (obj["$ref"] is { } reference)
            {
                obj.Remove("$ref");
                obj["oneOf"] = new JsonArray(
                    new JsonObject { ["$ref"] = reference.GetValue<string>() },
                    new JsonObject { ["type"] = "null" }
                );
            }
        }
        return obj;
    }
}
