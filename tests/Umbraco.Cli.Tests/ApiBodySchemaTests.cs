using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The JSON Schemas <c>--schema</c> prints for the raw schema verbs come from the Management API
/// spec the client is generated from, never a hand-written copy. They are committed as
/// <c>src/Umbraco.Cli/Schemas/api-bodies.json</c> (embedded in the CLI, so <c>--schema</c> needs no
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
        var root = RepoRoot();
        var path = Path.Combine(root, "src", "Umbraco.Cli", "Schemas", "api-bodies.json");
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
            "src/Umbraco.Cli/Schemas/api-bodies.json is out of date with spec/management.json. "
                + "Run `UPDATE_API_SCHEMAS=1 dotnet test --filter ApiBodySchemaTests` and commit it."
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
