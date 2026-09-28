using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// Shared behaviour for every live test class: the reachability skip, and scratch fixtures that
/// clean themselves up.
/// </summary>
/// <param name="live">Shared reachability fixture.</param>
public abstract class LiveTestBase(LiveInstanceFixture live)
{
    private readonly LiveInstanceFixture _live = live;

    /// <summary>
    /// Skips the current test unless a live instance responded to the probe.
    /// <para>
    /// This is the only legitimate use of <c>Skip</c> around a write: the environment cannot run
    /// the test at all. Skipping because a command under test did not do what it claimed turns the
    /// regression into a green result - see the note on <see cref="EffectIntegrationTests"/>.
    /// </para>
    /// </summary>
    protected void RequireLive() => Skip.IfNot(_live.IsReachable, _live.SkipReason);

    /// <summary>A prefix + random suffix, so fixtures never collide on a shared instance.</summary>
    /// <param name="prefix">A short identifying prefix.</param>
    /// <returns>A unique alias.</returns>
    protected static string ScratchAlias(string prefix) =>
        prefix + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Whether a failed command was the server saying the item does not exist (HTTP 404).
    /// <para>
    /// A bare "the command failed" would also pass on an auth or network error, so an assertion
    /// that something was deleted checks for the 404 in the JSON error envelope on stderr.
    /// </para>
    /// </summary>
    /// <param name="result">The result of a read such as <c>content get</c>.</param>
    /// <returns>True when the command failed with HTTP status 404.</returns>
    protected static bool IsNotFound(CliResult result)
    {
        if (result.Ok)
            return false;
        try
        {
            using var error = JsonDocument.Parse(result.Stderr);
            return error.RootElement.TryGetProperty("httpStatus", out var status)
                && status.ValueKind is JsonValueKind.Number
                && status.GetInt32() == 404;
        }
        catch (JsonException)
        {
            // Not an error envelope (e.g. a crash trace), so it is not a clean not-found.
            return false;
        }
    }
}

/// <summary>
/// A throwaway culture-variant document type for multi-variant content tests (#103), removed on
/// dispose together with every document of that type.
/// <para>
/// It varies by culture, is allowed at the root, allows itself as a child (so a small subtree can
/// be built from one type), and has a single culture-variant Textstring property,
/// <see cref="PropertyAlias"/>. Deleting it with <c>--force</c> also deletes its documents, so a
/// test only has to dispose this one scope to leave the instance as it found it.
/// </para>
/// </summary>
public sealed class ScratchCultureType : IDisposable
{
    /// <summary>The id of Umbraco's built-in Textstring data type, the same on every install.</summary>
    private const string TextstringDataTypeId = "0cc0eba1-9960-42c9-bf9b-60e150b429ae";

    /// <summary>The alias of the type's one culture-variant text property.</summary>
    public const string PropertyAlias = "title";

    private ScratchCultureType(string id, string alias)
    {
        Id = id;
        Alias = alias;
    }

    /// <summary>The document type's id.</summary>
    public string Id { get; }

    /// <summary>The document type's (random) alias.</summary>
    public string Alias { get; }

    /// <summary>
    /// Creates the document type, then lets it nest under itself.
    /// <para>
    /// The self-reference cannot go in the create body: Umbraco drops an allowed child type that
    /// does not exist yet, which on create includes the type itself. So it is added by a second,
    /// merging <c>document-type update</c>.
    /// </para>
    /// </summary>
    /// <returns>The scope, which deletes the type and its documents on dispose.</returns>
    public static ScratchCultureType Create()
    {
        var alias = "clitestVariant" + Guid.NewGuid().ToString("N")[..8];
        var id = Guid.NewGuid().ToString();
        var containerId = Guid.NewGuid().ToString();
        var created = CliRunner.RunWithInput(
            $$"""
            {
              "id": "{{id}}",
              "alias": "{{alias}}",
              "name": "{{alias}}",
              "icon": "icon-document",
              "allowedAsRoot": true,
              "variesByCulture": true,
              "properties": [{
                "id": "{{Guid.NewGuid()}}",
                "container": { "id": "{{containerId}}" },
                "sortOrder": 0,
                "alias": "{{PropertyAlias}}",
                "name": "Title",
                "variesByCulture": true,
                "dataType": { "id": "{{TextstringDataTypeId}}" }
              }],
              "containers": [{ "id": "{{containerId}}", "name": "Content", "type": "Group", "sortOrder": 0 }]
            }
            """,
            "document-type",
            "create",
            "--json-body",
            "-"
        );
        // An instance that will not take this document type is an environment limit.
        Skip.IfNot(created.Ok, $"Could not create the scratch document type: {created.Stderr}");

        // From here the type exists, so anything that throws must still clean it up.
        var scope = new ScratchCultureType(id, alias);
        try
        {
            var nest = CliRunner.RunWithInput(
                $$"""{"allowedDocumentTypes":[{"documentType":{"id":"{{id}}"},"sortOrder":0}]}""",
                "document-type",
                "update",
                id,
                "--json-body",
                "-"
            );
            Assert.True(nest.Ok, nest.Stderr);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a document of this type with a name and a <see cref="PropertyAlias"/> value in
    /// each of two cultures. It is left as a draft.
    /// </summary>
    /// <param name="parentId">The parent document's id, or null for the content root.</param>
    /// <param name="variants">Per culture, the variant's name and its property value.</param>
    /// <returns>The new document's id.</returns>
    public string CreateDocument(
        string? parentId,
        params (string Culture, string Name, string Value)[] variants
    )
    {
        // Built as JSON nodes rather than a string template so names and values are escaped.
        var body = new JsonObject
        {
            ["documentType"] = new JsonObject { ["id"] = Id },
            ["parent"] = parentId is null ? null : new JsonObject { ["id"] = parentId },
            ["variants"] = new JsonArray([
                .. variants.Select(v => new JsonObject
                {
                    ["culture"] = v.Culture,
                    ["name"] = v.Name,
                }),
            ]),
            ["values"] = new JsonArray([
                .. variants.Select(v => new JsonObject
                {
                    ["alias"] = PropertyAlias,
                    ["culture"] = v.Culture,
                    ["value"] = v.Value,
                }),
            ]),
        };
        var created = CliRunner.RunWithInput(
            body.ToJsonString(),
            "content",
            "create",
            "--json-body",
            "-"
        );
        Assert.True(created.Ok, created.Stderr);
        return created.Data().GetProperty("id").GetString()!;
    }

    /// <inheritdoc />
    // --force: the type still has documents, and deleting them with it is the point of the scope.
    public void Dispose() => CliRunner.Run("document-type", "delete", Id, "--force", "--yes");
}

/// <summary>
/// A throwaway document and the document type it needs, both removed on dispose.
/// <para>
/// Acquisition and registration-for-cleanup are the same statement, so there is no window in which
/// a later failure can strand a fixture on the shared live instance. Deleting the document type
/// also removes any remaining content of that type, so the document delete is best-effort.
/// </para>
/// </summary>
public sealed class ScratchDocument : IDisposable
{
    private ScratchDocument(string id, string documentTypeId, string documentTypeAlias)
    {
        Id = id;
        DocumentTypeId = documentTypeId;
        DocumentTypeAlias = documentTypeAlias;
    }

    /// <summary>The created document's id.</summary>
    public string Id { get; }

    /// <summary>The throwaway document type's id.</summary>
    public string DocumentTypeId { get; }

    /// <summary>The throwaway document type's alias.</summary>
    public string DocumentTypeAlias { get; }

    /// <summary>
    /// Creates a root-allowed document type and one document of it.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="template">
    /// Optional template to create the document with (#162). It is made the type's allowed and
    /// default template first, because Umbraco refuses a template the type doesn't allow
    /// (<c>TemplateNotAllowed</c>, #327).
    /// </param>
    /// <returns>The scope, which deletes both on dispose.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">When the type or the document can't be created.</exception>
    public static ScratchDocument Create(string name, ScratchTemplate? template = null)
    {
        var alias = "clitestEffect" + Guid.NewGuid().ToString("N")[..8];
        var type = CliRunner.Run(
            "document-type",
            "create",
            "--name",
            alias,
            "--alias",
            alias,
            "--allow-at-root"
        );
        Assert.True(type.Ok, type.Stderr);
        var typeId = type.Data().GetProperty("id").GetString()!;

        // From here the type exists, so anything that throws must still clean it up.
        try
        {
            if (template is not null)
            {
                // update merges top-level keys, so this changes only the type's templates.
                var allow = CliRunner.RunWithInput(
                    JsonSerializer.Serialize(
                        new
                        {
                            allowedTemplates = new[] { new { id = template.Id } },
                            defaultTemplate = new { id = template.Id },
                        }
                    ),
                    "document-type",
                    "update",
                    typeId,
                    "--json-body",
                    "-"
                );
                Assert.True(allow.Ok, $"Could not allow the scratch template: {allow.Stderr}");
            }

            string[] args = template is null
                ? ["content", "create", "--document-type", alias, "--name", name]
                :
                [
                    "content",
                    "create",
                    "--document-type",
                    alias,
                    "--name",
                    name,
                    "--template",
                    template.Alias,
                ];
            var document = CliRunner.Run(args);
            // A reachable instance that can't build the fixture is a failure, not a skip: skipping
            // here hid #327, and with it the #178 regression test, on every run.
            Assert.True(document.Ok, $"Could not create the scratch document: {document.Stderr}");
            return new ScratchDocument(
                document.Data().GetProperty("id").GetString()!,
                typeId,
                alias
            );
        }
        catch
        {
            CliRunner.Run("document-type", "delete", typeId, "--force", "--yes");
            throw;
        }
    }

    /// <summary>Whether the document currently reports as published.</summary>
    /// <returns>True when <c>content get</c> reports it published.</returns>
    public bool IsPublished()
    {
        var get = CliRunner.Run("content", "get", Id);
        Assert.True(get.Ok, get.Stderr);
        return get.Data().GetProperty("isPublished").GetBoolean();
    }

    /// <summary>
    /// The document's verbatim body, read back through <c>content export</c>.
    /// <para>
    /// <c>content get</c> still returns a narrow projection (#168, Phase 3), so the export snapshot
    /// is the only way to see a document's template and property values through the CLI. The
    /// document is selected by id rather than by position, so the assertion does not depend on the
    /// exporter's walk order.
    /// </para>
    /// </summary>
    /// <returns>The document's body.</returns>
    public JsonElement ExportBody()
    {
        var export = CliRunner.Run("content", "export", "--root", Id);
        Assert.True(export.Ok, export.Stderr);
        return export
            .Data()
            .GetProperty("documents")
            .EnumerateArray()
            .Single(d => d.GetProperty("id").GetString() == Id)
            .GetProperty("body");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CliRunner.Run("content", "delete", Id, "--yes");
        CliRunner.Run("document-type", "delete", DocumentTypeId, "--force", "--yes");
    }
}

/// <summary>A throwaway template, removed on dispose.</summary>
public sealed class ScratchTemplate : IDisposable
{
    private ScratchTemplate(string id, string alias)
    {
        Id = id;
        Alias = alias;
    }

    /// <summary>The created template's id.</summary>
    public string Id { get; }

    /// <summary>The created template's alias.</summary>
    public string Alias { get; }

    /// <summary>Creates a template with a random alias.</summary>
    /// <returns>The scope, which deletes it on dispose.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">When the template can't be created.</exception>
    public static ScratchTemplate Create()
    {
        var alias = "clitestTpl" + Guid.NewGuid().ToString("N")[..8];
        var created = CliRunner.Run("template", "create", "--name", alias, "--alias", alias);
        Assert.True(created.Ok, $"Could not create a scratch template: {created.Stderr}");
        return new ScratchTemplate(created.Data().GetProperty("id").GetString()!, alias);
    }

    /// <inheritdoc />
    // --force: cleanup must not be refused if a test left a document type pointing at it (#269).
    public void Dispose() => CliRunner.Run("template", "delete", Id, "--force", "--yes");
}
